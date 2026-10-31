using System.Reflection;
using System.Text.Json;
using Cvx.Formats;
using Cvx.Formats.Rdx;
using Cvx.Formats.Script;

namespace Cvx.Rando.Data;

public enum IssueSeverity { Info, Warning, Error }

public sealed record ValidationIssue(IssueSeverity Severity, string Room, string Message)
{
    public override string ToString() => $"{Severity,-7} {Room,-7} {Message}";
}

/// <summary>A room in RDX_LNK.AFS that the room data does not describe.</summary>
public sealed record UnmappedRoom(RoomId Room, int Doors, int ItemPickups, int Items, int Enemies, int Entrances);

public sealed class ValidationReport
{
    public List<ValidationIssue> Issues { get; } = [];
    public List<UnmappedRoom> UnmappedRooms { get; } = [];
    public int RoomsChecked { get; set; }
    public int DoorsChecked { get; set; }
    public int ItemsChecked { get; set; }
    public int OffsetsChecked { get; set; }

    public IEnumerable<ValidationIssue> Errors => Issues.Where(x => x.Severity == IssueSeverity.Error);
    public IEnumerable<ValidationIssue> Warnings => Issues.Where(x => x.Severity == IssueSeverity.Warning);
}

/// <summary>
/// Checks the room data (BioRand's rdt.json) against the game's room files: every trigger area,
/// item and script offset it names must be what it says, since the randomizer patches them.
/// </summary>
public sealed class DataValidator
{
    /// <summary>Trigger area type 3: examine (a message or an event script).</summary>
    private const byte AotExamine = 3;

    private readonly RandoData _data;
    private readonly RoomArchive _rooms;
    private readonly Dictionary<RoomId, RdxFile> _cache = [];

    public DataValidator(RandoData data, RoomArchive rooms)
    {
        _data = data;
        _rooms = rooms;
    }

    private RdxFile Room(RoomId id)
    {
        lock (_cache)
        {
            if (!_cache.TryGetValue(id, out var file))
                _cache[id] = file = _rooms.ReadRoom(id);
            return file;
        }
    }

    public ValidationReport Validate()
    {
        var report = new ValidationReport();
        CheckJsonKeys(report);

        var map = _data.Map;
        foreach (var be in map.BeginEndRooms)
        {
            foreach (var r in new[] { be.Start, be.End })
            {
                if (!TryRoom(r, out _))
                    report.Issues.Add(new(IssueSeverity.Error, "-", $"beginEndRooms: room {r} does not exist"));
            }
        }

        var results = new List<(string Key, List<ValidationIssue> Issues, int Doors, int Items, int Offsets)>();
        Parallel.ForEach(map.Rooms, kv =>
        {
            var issues = new List<ValidationIssue>();
            var counts = CheckRoom(kv.Key, kv.Value, issues);
            lock (results)
                results.Add((kv.Key, issues, counts.Doors, counts.Items, counts.Offsets));
        });
        foreach (var r in results.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            report.Issues.AddRange(r.Issues);
            report.DoorsChecked += r.Doors;
            report.ItemsChecked += r.Items;
            report.OffsetsChecked += r.Offsets;
        }
        report.RoomsChecked = map.Rooms.Count;

        foreach (var pos in _data.EnemyPositions)
        {
            if (!TryRoom(pos.Room, out _))
                report.Issues.Add(new(IssueSeverity.Error, pos.Room, "enemy.json: room does not exist"));
        }

        var mapped = map.Rooms.Keys.Select(k => TryRoom(k, out var id) ? id : default).ToHashSet();
        foreach (var id in RoomTable.All.Where(x => !mapped.Contains(x)))
        {
            var file = Room(id);
            var aots = file.Aots;
            report.UnmappedRooms.Add(new UnmappedRoom(id,
                Count(aots, RdxAot.KindDoor), Count(aots, RdxAot.KindItem),
                file.Items.Length, file.Enemies.Length, file.Players.Length));
        }

        return report;
    }

    private static int Count(ReadOnlySpan<RdxAot> aots, byte kind)
    {
        var n = 0;
        foreach (var a in aots)
        {
            if (a.Kind == kind)
                n++;
        }
        return n;
    }

    private static bool TryRoom(string? bioRandId, out RoomId id)
    {
        id = default;
        return bioRandId != null && RoomId.TryParseBioRand(bioRandId, out id) && RoomTable.Contains(id);
    }

    private (int Doors, int Items, int Offsets) CheckRoom(string key, MapRoom room, List<ValidationIssue> issues)
    {
        void Error(string m) => issues.Add(new(IssueSeverity.Error, key, m));
        void Warn(string m) => issues.Add(new(IssueSeverity.Warning, key, m));

        if (!TryRoom(key, out var id))
        {
            Error("room does not exist in RDX_LNK.AFS");
            return default;
        }

        var file = Room(id);
        var items = file.Items.ToArray();
        var opcodes = CvScriptReader.ReadOpcodes(file);
        var byOffset = opcodes.ToDictionary(x => x.Offset);
        var aots = AotStates.Read(file, opcodes);
        int doors = 0, itemCount = 0, offsets = 0;

        foreach (var r in room.Requires ?? [])
            CheckItemId(r, "room requires", Warn);

        foreach (var door in room.Doors ?? [])
        {
            doors++;
            var label = $"door {door.Id}" + (door.Target != null ? $" -> {door.Target}" : "");
            RoomId target = default;
            if (door.Target == null || !TryRoom(door.Target, out target))
            {
                Error($"{label}: target room does not exist");
            }
            else if (door.EntranceId is int e && e >= Room(target).Players.Length)
            {
                // the target's script can place the player itself for higher entrance numbers
                // (it reads pos_no, script variable 15)
                issues.Add(new(IssueSeverity.Info, key, $"{label}: target has {Room(target).Players.Length} entrance positions, entrance {e} is placed by its script"));
            }

            foreach (var r in door.Requires ?? [])
                CheckItemId(r, label + " requires", Warn);
            foreach (var r in door.RequiresRoom ?? [])
            {
                if (!TryRoom(r, out _))
                    Error($"{label}: requiresRoom {r} does not exist");
            }

            if (door.Offsets is { Length: > 0 })
            {
                foreach (var spec in door.Offsets)
                {
                    offsets++;
                    if (!byOffset.TryGetValue(spec.Start, out var op))
                        Error($"{label}: no script opcode starts at {spec}");
                    else if (op.Opcode is not (CvOpcodes.SetDoorCall or CvOpcodes.RoomCaseNo or CvOpcodes.RoomSoundCase))
                        Error($"{label}: {spec} is {op.Name}, not a door opcode");
                    else if (op.Opcode == CvOpcodes.SetDoorCall && door.Target != null &&
                             (op.Bytes.Span[4] != target.Stage || op.Bytes.Span[5] != target.Room))
                        Warn($"{label}: set_door_call at {spec} goes to stage {op.Bytes.Span[4]} room {op.Bytes.Span[5]}");
                    else if (op.Opcode == CvOpcodes.SetDoorCall && door.Id is int used && used != 255 &&
                             UsedArea(opcodes, op) is int checkedArea && checkedArea != used)
                        Error($"{label}: set_door_call at {spec} runs when trigger area {checkedArea} is used (ck 0A 17 00 {checkedArea:X2}), not {used}");
                }
            }
            else if (door.Id is int aotId && aotId != 255)
            {
                foreach (var a in door.AltId is int alt ? new[] { aotId, alt } : [aotId])
                {
                    // the room's own trigger area, or what its script turns it into
                    var states = aots.Get(a);
                    var asDoor = states.Where(s => s.Type == RdxAot.KindDoor).ToList();
                    if (states.Count == 0)
                    {
                        Error($"{label}: room has {aots.StaticCount} trigger areas, no {a}, and no script makes it");
                    }
                    else if (asDoor.Count == 0 && states.Any(s => s.Type == AotExamine))
                    {
                        // an examine area whose event script calls the door (set_door_call)
                        var calls = opcodes.Where(o => o.Opcode == CvOpcodes.SetDoorCall).ToList();
                        if (door.Target != null && !calls.Any(o => o.Bytes.Span[4] == target.Stage && o.Bytes.Span[5] == target.Room))
                            Warn($"{label}: trigger area {a} is an event and no set_door_call in the room goes to the target");
                        else
                            issues.Add(new(IssueSeverity.Info, key, $"{label}: trigger area {a} is an event that calls the door"));
                    }
                    else if (asDoor.Count == 0)
                    {
                        Error($"{label}: trigger area {a} is never a door ({string.Join(", ", states.Select(s => s.Describe()))})");
                    }
                    else if (door.Target != null && !asDoor.Any(s => s.Prm0 == target.Stage && s.Prm1 == target.Room))
                        Error($"{label}: trigger area {a} goes to {string.Join(", ", asDoor.Select(s => s.Describe()))}");
                }
            }
            else if (door.Id is null)
            {
                // logic-only connection (no door in this room to patch)
                issues.Add(new(IssueSeverity.Info, key, $"{label}: no trigger area or script offsets (logic only)"));
            }

            // entranceId is where you arrive in THIS room through this door (BioRand writes it
            // into whatever door gets connected here), so the doors leading back must use it.
            if (door.EntranceId is int arrive && door.Target != null && RoomTable.Contains(target))
            {
                var back = BackDoorEntrances(target, id);
                if (back.Count != 0 && !back.Contains(arrive))
                    Warn($"{label}: arrives at entrance {arrive}, but doors from {door.Target} come in at {string.Join("/", back)}");
            }
        }

        foreach (var item in room.Items ?? [])
        {
            itemCount++;
            var label = $"item {item.Id}" + (item.GlobalId is int g ? $" (global {g})" : "");
            if (item.Type is not int type)
            {
                Error($"{label}: no type");
                continue;
            }
            label += $" {_data.Items.NameOf(type)}";
            CheckItemId(type, label, Error);
            foreach (var r in item.Requires ?? [])
                CheckItemId(r, label + " requires", Warn);

            // As BioRand: the room's item table is patched unless the item lives only in script
            // offsets (then it must name its item slot with itemId).
            var hasOffsets = item.Offsets is { Length: > 0 };
            if (item.ItemId is int slot)
            {
                CheckItemSlot(slot);
            }
            else if (!hasOffsets)
            {
                var all = aots.Get(item.Id);
                var states = all.Where(s => s.Type == RdxAot.KindItem).ToList();
                if (states.Count == 0)
                {
                    var docs = _data.Items.Get(type)?.IsDocument == true && all.Any(s => s.Type == AotExamine);
                    if (docs)
                        issues.Add(new(IssueSeverity.Info, key, $"{label}: document read through an event (not randomized)"));
                    else
                        Error($"{label}: trigger area {item.Id} is never an item ({string.Join(", ", all.Select(s => s.Describe()))})");
                }
                else if (!states.Any(s => s.Prm0 < items.Length && items[s.Prm0].ItemId == type))
                {
                    var slots = states.Select(s => s.Prm0).Distinct().Select(i =>
                        i < items.Length ? $"slot {i} {_data.Items.NameOf(items[i].ItemId)}" : $"slot {i} (none)");
                    Error($"{label}: trigger area {item.Id} picks up {string.Join(", ", slots)}");
                }
            }

            void CheckItemSlot(int index)
            {
                if (index >= items.Length)
                    Error($"{label}: room has {items.Length} items, no item {index}");
                else if (items[index].ItemId != type)
                    Error($"{label}: room item {index} is {_data.Items.NameOf(items[index].ItemId)}");
            }

            foreach (var spec in item.Offsets ?? [])
            {
                offsets++;
                if (!byOffset.TryGetValue(spec.Start, out var op))
                {
                    Error($"{label}: no script opcode starts at {spec}");
                    continue;
                }
                var positions = CvOpcodes.ItemPositions(op);
                if (positions.Length == 0)
                    Error($"{label}: {spec} is {op.Name}, which does not hold an item");
                else if (!positions.Any(p => op.Bytes.Span[p] == type))
                    Error($"{label}: {op.Name} at {spec} holds {string.Join("/", positions.Select(p => _data.Items.NameOf(op.Bytes.Span[p])))}");
            }

            CheckNop(item.Nop, label, opcodes, file, Error);
            if (TakenAtStart(opcodes, item.Id) is { } taken)
                Error($"{label}: the room marks this pickup taken when it starts ({taken.Name} at 0x{taken.Offset:X}), so it never shows");
        }

        foreach (var dr in room.DoorRando ?? [])
        {
            CheckNop(dr.Nop, "doorRando", opcodes, file, Error);
            CheckDoorSetups(dr.Nop, opcodes, file, Error);
        }
        foreach (var en in room.Enemies ?? [])
            CheckNop(en.Nop, "enemies", opcodes, file, Error);

        return (doors, itemCount, offsets);
    }

    /// <summary>
    /// The <c>set</c> of item flag group 7 by which a room's start procedure marks the pickup of
    /// trigger area <paramref name="area"/> taken whatever happened (outside any <c>if</c>), or
    /// null: such a pickup never shows (RM_1070 area 7, RM_9290 area 9).
    /// </summary>
    internal static CvOpcode? TakenAtStart(IReadOnlyList<CvOpcode> opcodes, int area)
    {
        const byte SetCkItem = 0x23;    // set_ck_item work, flag (2 bytes), area, keep
        var start = opcodes.Where(o => o.Procedure == 0).ToList();
        var pickup = start.FirstOrDefault(o => o.Opcode == SetCkItem && o.Length >= 6 && o.Bytes.Span[4] == area);
        if (pickup.Bytes.IsEmpty)
            return null;
        var flag = pickup.Bytes.Span[2] | pickup.Bytes.Span[3] << 8;
        var set = start.FirstOrDefault(o => o.Name == "set" && o.Length >= 6 && o.Bytes.Span[1] == 7 &&
                                            (o.Bytes.Span[2] | o.Bytes.Span[3] << 8) == flag && o.Bytes.Span[5] == 0 &&
                                            !start.Any(b => b.Name is "if" or "else" && b.Offset < o.Offset && b.Offset + b.Bytes.Span[1] > o.Offset));
        return set.Bytes.IsEmpty ? null : set;
    }

    private static void CheckNop(OffsetSpec[]? specs, string label, IReadOnlyList<CvOpcode> opcodes, RdxFile file, Action<string> error)
    {
        var scriptStart = file.ScriptOffset;
        var scriptEnd = scriptStart + file.Script.Data.Length;
        foreach (var spec in specs ?? [])
        {
            if (spec.Start < scriptStart || spec.End >= scriptEnd)
                error($"{label}: nop {spec} is outside the scripts (0x{scriptStart:X}-0x{scriptEnd - 1:X})");
            else if (!opcodes.Any(o => spec.Contains(o.Offset)))
                error($"{label}: nop {spec} covers no opcode");
            else if (!spec.IsRange && !opcodes.Any(o => o.Offset == spec.Start))
                error($"{label}: nop {spec} is not the start of an opcode");
        }
    }

    /// <summary>
    /// A door mode nop must not remove the only line that makes a trigger area a door: the room
    /// file's area is then not a door, and nothing else sets it up (RM_0110 #11 lost its door
    /// to RM_0120 when a range reached one opcode past an <c>else</c>).
    /// </summary>
    private static void CheckDoorSetups(OffsetSpec[]? specs, IReadOnlyList<CvOpcode> opcodes, RdxFile file, Action<string> error)
    {
        var aots = file.Aots;
        bool MakesDoor(CvOpcode o) => o.Opcode == CvOpcodes.SetEtcAtari2 && o.Length >= 9 && o.Bytes.Span[8] == RdxAot.KindDoor;
        foreach (var spec in specs ?? [])
        {
            foreach (var op in opcodes.Where(o => spec.Contains(o.Offset) && MakesDoor(o)))
            {
                var area = op.Bytes.Span[1];
                var fileDoor = area < aots.Length && aots[area].Kind == RdxAot.KindDoor;
                var kept = opcodes.Any(o => !(specs ?? []).Any(s => s.Contains(o.Offset)) && MakesDoor(o) && o.Bytes.Span[1] == area);
                if (!fileDoor && !kept)
                    error($"doorRando: nop {spec} removes every door setup of trigger area {area} (0x{op.Offset:X})");
            }
        }
    }

    /// <summary>
    /// The trigger area a script door is called from: the nearest check before it in its
    /// procedure of which area the player used (<c>ck 0A 17 00 xx 00</c>: etc_idx is xx). recvx-pc
    /// looks script doors up by that area (room.c passes sys->etc_idx), so a door whose id is
    /// another area is never redirected (RM_7120's door to RM_7230 is area 4, not 3).
    /// </summary>
    private static int? UsedArea(IReadOnlyList<CvOpcode> opcodes, CvOpcode call)
    {
        // the open if blocks around the call, each with the area its conditions test (null once
        // in its else branch, where they do not hold)
        var blocks = new Stack<(int? Area, bool Conditions)>();
        foreach (var op in opcodes.Where(o => o.Procedure == call.Procedure))
        {
            if (op.Offset == call.Offset)
                return blocks.Select(b => b.Area).FirstOrDefault(a => a != null);
            var b = op.Bytes.Span;
            switch (op.Name)
            {
                case "if":
                    blocks.Push((null, true));
                    continue;
                case "ck" when blocks.Count != 0 && blocks.Peek().Conditions:
                    if (b.Length >= 6 && b[1] == 0x0A && b[2] == 0x17 && b[5] == 0)
                        blocks.Push((b[4], blocks.Pop().Conditions));
                    continue;
                case "else":
                    if (blocks.Count != 0)
                    {
                        blocks.Pop();
                        blocks.Push((null, false));
                    }
                    continue;
                case "endif":
                    if (blocks.Count != 0)
                        blocks.Pop();
                    continue;
            }
            if (blocks.Count != 0 && blocks.Peek().Conditions)
                blocks.Push((blocks.Pop().Area, false));   // the block's body starts
        }
        return null;
    }

    /// <summary>Entrances that the doors of <paramref name="from"/> leading to <paramref name="to"/> arrive at.</summary>
    private HashSet<int> BackDoorEntrances(RoomId from, RoomId to)
    {
        var file = Room(from);
        var states = AotStates.Read(file, CvScriptReader.ReadOpcodes(file));
        var result = new HashSet<int>();
        for (var i = 0; i < 256; i++)
        {
            foreach (var s in states.Get(i))
            {
                if (s.Type == RdxAot.KindDoor && s.Prm0 == to.Stage && s.Prm1 == to.Room)
                    result.Add(s.Prm2);
            }
        }
        foreach (var op in CvScriptReader.ReadOpcodes(file))
        {
            if (op.Opcode == CvOpcodes.SetDoorCall && op.Bytes.Span[4] == to.Stage && op.Bytes.Span[5] == to.Room)
                result.Add(op.Bytes.Span[6]);
        }
        return result;
    }

    private void CheckItemId(int id, string label, Action<string> report)
    {
        if (_data.Items.Get(id) == null)
            report($"{label}: unknown item {id}");
    }

    /// <summary>Reports JSON keys that the model would silently drop.</summary>
    private static void CheckJsonKeys(ValidationReport report)
    {
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        using var doc = JsonDocument.Parse(RandoData.ReadResourceText("data/recv/rdt.json"), options);
        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var room in doc.RootElement.GetProperty("rooms").EnumerateObject())
        {
            Collect(room.Value, typeof(MapRoom), "room", unknown);
            foreach (var (name, type) in new[] { ("doors", typeof(MapRoomDoor)), ("items", typeof(MapRoomItem)),
                         ("enemies", typeof(MapRoomEnemies)), ("doorRando", typeof(DoorRandoSpec)) })
            {
                if (room.Value.TryGetProperty(name, out var array))
                {
                    foreach (var e in array.EnumerateArray())
                        Collect(e, type, name, unknown);
                }
            }
        }
        foreach (var u in unknown)
            report.Issues.Add(new(IssueSeverity.Warning, "-", $"rdt.json key not read by the randomizer: {u}"));
    }

    private static void Collect(JsonElement e, Type type, string where, SortedSet<string> unknown)
    {
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var p in e.EnumerateObject())
        {
            if (!props.Contains(p.Name))
                unknown.Add($"{where}.{p.Name}");
        }
    }
}
