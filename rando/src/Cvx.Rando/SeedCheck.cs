using System.Globalization;
using Cvx.Formats;
using Cvx.Formats.Rdx;
using Cvx.Formats.Script;
using Cvx.Rando.Data;
using Cvx.Rando.Doors;
using Cvx.Rando.Items;

namespace Cvx.Rando;

/// <summary>
/// Checks a written seed: the item logic plays it through (<see cref="Playthrough"/>), and the
/// mod folder holds what the logic counted on. Every pickup's room file in the written
/// RDX_LNK.AFS has its new item (item table and script), every door of the plan is in
/// rando_doors.txt and no open door there leads out of the run, and rando.ini starts the
/// player with the inventory the logic used.
/// </summary>
public static class SeedCheck
{
    /// <summary>The problems found; empty when the seed is good.</summary>
    /// <param name="original">The game's own rooms the seed was made from.</param>
    public static List<string> Check(GenerationResult result, RandoData data, RoomArchive original, string directory)
    {
        var problems = new List<string>();
        var settings = result.Settings;
        if (result.Items is { } placement)
        {
            var graph = ItemGraph.Build(data, settings, result.Doors);
            var play = Playthrough.Check(graph, placement);
            if (!play.Completed)
                problems.Add($"logic: {play.Message}");
            else if (Playthrough.FindTrap(graph, placement) is { } trap)
                problems.Add($"logic: {trap}");
            if (placement.ByGlobalId.Count != graph.Locations.Count)
                problems.Add($"logic: {graph.Locations.Count - placement.ByGlobalId.Count} pickups left empty");

            var afs = Path.Combine(directory, GameData.RoomArchiveName);
            if (!File.Exists(afs))
                problems.Add($"{GameData.RoomArchiveName} was not written");
            else
                problems.AddRange(CheckRooms(result, data, original, afs, placement));
        }
        if (result.Doors is { } plan)
            problems.AddRange(CheckDoors(plan, Path.Combine(directory, DoorTable.FileName)));
        problems.AddRange(CheckIni(result, Path.Combine(directory, "rando.ini")));
        return problems;
    }

    private static IEnumerable<string> CheckRooms(GenerationResult result, RandoData data, RoomArchive original, string afs,
        ItemPlacementResult placement)
    {
        using var written = AfsArchive.Open(afs);
        foreach (var (key, room) in data.Map.Rooms)
        {
            var id = RoomId.ParseBioRand(key);
            // (pickups the logic left alone, documents, are not changed)
            var items = (room.Items ?? []).Where(i => i.GlobalId is int g && i.Type != null && placement.ByGlobalId.ContainsKey(g) &&
                (i.DoorRando == null || i.DoorRando == result.Settings.RandomDoors)).ToList();
            if (items.Count == 0)
                continue;
            var file = new RdxFile(Prs.Decompress(written.ReadEntry(RoomTable.IndexOf(id))));
            var ops = CvScriptReader.ReadOpcodes(file);
            IReadOnlyList<CvOpcode>? before = null;
            foreach (var item in items)
            {
                var expect = placement.ByGlobalId[item.GlobalId!.Value].Type;
                var name = data.Items.NameOf(expect);
                var hasOffsets = item.Offsets is { Length: > 0 };
                if (item.ItemId is int || !hasOffsets)
                {
                    var slot = item.ItemId ?? ItemPatcher.ResolveSlot(file, item.Id, expect);
                    if (slot is not int s || s >= file.Items.Length)
                        yield return $"{key}#{item.GlobalId}: no item slot holds {name}";
                    else if (file.Items[s].ItemId != expect)
                        yield return $"{key}#{item.GlobalId}: item slot {s} holds {data.Items.NameOf(file.Items[s].ItemId)}, the logic put {name} there";
                }
                if (!hasOffsets)
                    continue;

                // Script offsets are the original file's: rooms rebuilt later (opcodes added to
                // their start, enemy and NPC swaps) moved them. Find the same opcode by its
                // place among the procedure's opcodes of its kind.
                before ??= CvScriptReader.ReadOpcodes(original.ReadRoom(id));
                foreach (var spec in item.Offsets!)
                {
                    if (item.Nop?.Any(n => n.Contains(spec.Start)) == true)
                        continue;
                    var op = Find(before, ops, spec.Start);
                    if (op == null)
                        yield return $"{key}#{item.GlobalId}: the script opcode at {spec} is gone";
                    else if (!CvOpcodes.ItemPositions(op.Value).Any(p => op.Value.Bytes.Span[p] == expect))
                        yield return $"{key}#{item.GlobalId}: {op.Value.Name} at {spec} does not give {name}";
                }
            }
        }
    }

    /// <summary>The written file's opcode that was at <paramref name="offset"/> in the original room.</summary>
    private static CvOpcode? Find(IReadOnlyList<CvOpcode> before, IReadOnlyList<CvOpcode> after, int offset)
    {
        var index = before.ToList().FindIndex(o => o.Offset == offset);
        if (index < 0)
            return null;
        var op = before[index];
        var same = before.Where(o => o.Procedure == op.Procedure && o.Opcode == op.Opcode).ToList();
        var now = after.Where(o => o.Procedure == op.Procedure && o.Opcode == op.Opcode).ToList();
        // (opcodes added to a procedure's start come before the original ones)
        var ordinal = same.IndexOf(op) + now.Count - same.Count;
        return ordinal >= 0 && ordinal < now.Count ? now[ordinal] : null;
    }

    private static IEnumerable<string> CheckDoors(DoorPlan plan, string path)
    {
        if (!File.Exists(path))
        {
            yield return $"{DoorTable.FileName} was not written";
            yield break;
        }
        var entries = File.ReadAllLines(path).Where(l => l.Length != 0 && l[0] != '#')
            .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray())
            .ToList();
        var run = plan.Rooms.Select(RoomId.ParseBioRand).ToHashSet();
        foreach (var link in plan.Links.Values)
        {
            var room = RoomId.ParseBioRand(link.Door.Room);
            var to = RoomId.ParseBioRand(link.TargetRoom);
            var found = entries.Any(e => e[0] == room.Stage && e[1] == room.Room && e[2] == room.Variant && e[3] == link.Door.Area &&
                e[6] == to.Stage && e[7] == to.Room && e[8] == to.Variant && e[9] == link.Arrival && (e.Length > 10 && e[10] == 1) == link.Locked);
            if (!found)
                yield return $"{DoorTable.FileName}: no line for {link.Door.Room} door {link.Door.Index} to {link.TargetRoom}";
        }
        foreach (var e in entries)
        {
            var locked = e.Length > 10 && e[10] == 1;
            if (!locked && !run.Contains(new RoomId(e[6], e[7], e[8])))
                yield return $"{DoorTable.FileName}: an open door of stage {e[0]} room {e[1]} leads out of the run (stage {e[6]} room {e[7]} case {e[8]})";
        }
    }

    private static IEnumerable<string> CheckIni(GenerationResult result, string path)
    {
        if (!File.Exists(path))
        {
            yield return "rando.ini was not written";
            yield break;
        }
        var lines = File.ReadAllLines(path);
        foreach (var inventory in result.StartInventories)
        {
            var want = string.Join(",", inventory.Items.Select(i => string.Create(CultureInfo.InvariantCulture, $"{i.Type}:{i.Count}")));
            if (!lines.Contains($"start_items_{inventory.Character}={want}"))
                yield return $"rando.ini: {inventory.Character}'s starting items are not the ones the logic used";
        }
    }
}
