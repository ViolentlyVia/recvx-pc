using System.Collections.Concurrent;
using System.Text;
using Cvx.Formats;
using Cvx.Formats.Rdx;
using Cvx.Formats.Script;
using Cvx.Rando.Data;

namespace Cvx.Rando.Doors;

/// <summary>Writes rando_doors.txt for recvx-pc (pc_rando.c, read_doors).</summary>
public static class DoorTable
{
    public const string FileName = "rando_doors.txt";

    private const byte SetEtcAtari2 = 0x25;

    /// <summary>One line of the table.</summary>
    public readonly record struct Entry(int Stage, int Room, int Case, int Area, int OldStage, int OldRoom,
        int NewStage, int NewRoom, int NewCase, int Entrance, bool Locked)
    {
        public override string ToString() =>
            $"{Stage} {Room} {Case} {Area} {OldStage} {OldRoom}  {NewStage} {NewRoom} {NewCase} {Entrance}{(Locked ? " 1" : "")}";
    }

    /// <param name="archive">
    /// The rooms, to lock every other door of the run's rooms: doors the door mode's data does
    /// not list (BioRand blanks the scripts of some; this randomizer keeps scripts with doors in
    /// them, as recvx-pc redirects doors in the engine) would otherwise lead out of the run.
    /// </param>
    public static string Write(DoorPlan plan, RandoData data, RoomArchive? archive = null) =>
        Format(Entries(plan, data, archive));

    public static string Format(IEnumerable<Entry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Written by cvx-rando; read by recvx.exe (pc_rando.c).");
        sb.AppendLine("# stage room case area old_stage old_room  new_stage new_room new_case entrance [1: locked, leads nowhere]");
        foreach (var e in entries)
            sb.AppendLine(e.ToString());
        return sb.ToString();
    }

    public static List<Entry> Entries(DoorPlan plan, RandoData data, RoomArchive? archive = null)
    {
        var entries = new List<Entry>();
        var run = plan.Rooms.Select(RoomId.ParseBioRand).ToHashSet();
        foreach (var link in plan.Links.Values.OrderBy(l => l.Door.Room, StringComparer.Ordinal).ThenBy(l => l.Door.Index))
        {
            var room = RoomId.ParseBioRand(link.Door.Room);
            var oldTarget = RoomId.ParseBioRand(link.Door.OriginalTarget);
            var to = RoomId.ParseBioRand(link.TargetRoom);

            // The room file the plan uses, and its other versions the run does not go through
            // itself: the game loads those by story flags, and their doors must go the same way.
            var cases = new List<int> { room.Variant };
            for (var c = 0; c <= 2; c++)
            {
                var variant = room with { Variant = c };
                if (c != room.Variant && RoomTable.Contains(variant) && !run.Contains(variant))
                    cases.Add(c);
            }
            foreach (var c in cases)
                entries.Add(new Entry(room.Stage, room.Room, c, link.Door.Area, oldTarget.Stage, oldTarget.Room,
                    to.Stage, to.Room, to.Variant, link.Arrival, link.Locked));
        }
        if (archive != null)
            entries.AddRange(Unlisted(run, entries, archive));
        return entries;
    }

    /// <summary>
    /// Locked entries for the doors of the run's room files that no entry covers and that lead
    /// out of the run: door trigger areas (in the room, or made by <c>set_etc_atari2</c> with
    /// type 0) and script doors the player opens (<c>set_door_call</c> when a trigger area is
    /// used). Seed 428946: RM_2060's script door to RM_2050, which BioRand's door mode blanks,
    /// led into the rest of the game. Doors into the run's rooms are ones the plan keeps, and
    /// cutscenes' door calls (the ending's) are left alone. A locked door area says it is locked;
    /// a locked script door leads back into its room.
    /// </summary>
    private static IEnumerable<Entry> Unlisted(IReadOnlySet<RoomId> run, List<Entry> entries, RoomArchive archive)
    {
        var files = RoomTable.All.Where(id => run.Any(r => r.Stage == id.Stage && r.Room == id.Room)).ToList();
        var found = new ConcurrentBag<Entry>();
        Parallel.ForEach(files, id =>
        {
            var file = archive.ReadRoom(id);
            var doors = new HashSet<(int Area, int Stage, int Room)>();
            var aots = file.Aots.ToArray();
            for (var i = 0; i < aots.Length; i++)
            {
                if (aots[i].Kind == RdxAot.KindDoor)
                    doors.Add((i, aots[i].Stage, aots[i].Room));
            }
            var ops = CvScriptReader.ReadOpcodes(file).ToList();
            foreach (var op in ops)
            {
                var b = op.Bytes.Span;
                if (op.Opcode == SetEtcAtari2 && b.Length >= 9 && b[8] == RdxAot.KindDoor)
                    doors.Add((b[1], b[4], b[5]));
                else if (op.Opcode == CvOpcodes.SetDoorCall && b.Length >= 6 && UsedArea(ops, op.Offset) is int used)
                    doors.Add((used, b[4], b[5]));   // (a door call without one is a cutscene's: left alone)
            }
            foreach (var (area, stage, room) in doors)
            {
                if ((stage == id.Stage && room == id.Room) || !RoomTable.All.Any(r => r.Stage == stage && r.Room == room))
                    continue;   // inside the room, or not a room
                if (run.Any(r => r.Stage == stage && r.Room == room))
                    continue;   // into the run: a door the plan keeps as it is
                var covered = entries.Any(e => e.Stage == id.Stage && e.Room == id.Room && e.Case == id.Variant &&
                    e.OldStage == stage && e.OldRoom == room && (area == 255 || e.Area == area || e.Area == 255));
                if (!covered)
                    found.Add(new Entry(id.Stage, id.Room, id.Variant, area, stage, room, id.Stage, id.Room, id.Variant, 0, true));
            }
        });
        return found.Distinct().OrderBy(e => e.Stage).ThenBy(e => e.Room).ThenBy(e => e.Case).ThenBy(e => e.Area).ThenBy(e => e.OldStage).ThenBy(e => e.OldRoom);
    }

    /// <summary>
    /// The trigger area a script door call waits for (an <c>if</c> around it checks that the
    /// player used it: <c>ck 0A:17 == area</c>), or null (a cutscene's door call).
    /// </summary>
    private static int? UsedArea(List<CvOpcode> ops, int offset)
    {
        foreach (var c in Items.StoryFlags.IfConditions(ops, offset))
        {
            var b = c.Bytes.Span;
            if (c.Name == "ck" && b.Length >= 6 && b[1] == 0x0A && b[2] == 0x17 && b[3] == 0)
                return b[4];
        }
        return null;
    }
}
