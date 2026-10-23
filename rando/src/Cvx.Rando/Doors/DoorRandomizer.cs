// Segments and segment size after BioRand's DoorRandomiser.CreateRandomGraph (MIT, Copyright
// (c) 2022-2026 Ted John), rewritten for Code: Veronica's door redirects.

using Cvx.Formats;
using Cvx.Rando.Data;

namespace Cvx.Rando.Doors;

/// <summary>One door of a room in the room data (the door mode's entries).</summary>
public sealed class DoorStub
{
    public required string Room { get; init; }
    public required int Index { get; init; }
    public required MapRoomDoor Raw { get; init; }

    public string OriginalTarget => Raw.Target!;
    /// <summary>Where the player arrives in <see cref="Room"/> when coming in through this door.</summary>
    public int Arrival => Raw.EntranceId ?? 0;
    /// <summary>The trigger area the engine matches (255: any, for script-only doors).</summary>
    public int Area => Raw.Id ?? 255;
    public int[] Requires => Raw.Requires ?? [];
    /// <summary>The engine cannot redirect it: no trigger area and no script offsets.</summary>
    public bool Fixed => Raw.Id == null && Raw.Offsets == null;

    public override string ToString() => $"{Room}#{Raw.Id?.ToString() ?? "-"}->{OriginalTarget}";
}

/// <summary>Where a door leads after randomizing (to its own room when it is a dead end).</summary>
public sealed record DoorLink(DoorStub Door, string TargetRoom, int Arrival, DoorStub? Partner)
{
    /// <summary>
    /// A dead end the player opens by hand (a door area: no script offsets, 255 is a script door): the game shows it locked
    /// instead of taking the player through it back into the same room. Doors scripts open
    /// still lead back into their room.
    /// </summary>
    public bool Locked => Partner == null && TargetRoom == Door.Room && Door.OriginalTarget != Door.Room && Door.Area != 255 &&
                          Door.Raw.Offsets is not { Length: > 0 };
}

/// <summary>One part of a run: from its entry to its exit (a one-way story door, or the end).</summary>
public sealed record DoorSegment(string Entry, string Exit, IReadOnlyList<string> Rooms);

/// <summary>The door layout of a seed.</summary>
public sealed class DoorPlan
{
    public required Character Character { get; init; }
    public required string StartRoom { get; init; }
    public required int StartEntrance { get; init; }
    public required string EndRoom { get; init; }
    public required IReadOnlyDictionary<DoorStub, DoorLink> Links { get; init; }
    /// <summary>The parts of the run in play order; together, every room the run uses.</summary>
    public required IReadOnlyList<DoorSegment> Segments { get; init; }

    private HashSet<string>? _rooms;

    /// <summary>The rooms the run uses (the others are never entered).</summary>
    public IReadOnlySet<string> Rooms => _rooms ??= Segments.SelectMany(s => s.Rooms).ToHashSet(StringComparer.Ordinal);

    public DoorLink? Get(DoorStub stub) => Links.GetValueOrDefault(stub);

    private Dictionary<(string, int), DoorLink>? _byIndex;

    /// <summary>The link for door <paramref name="index"/> of <paramref name="room"/> (its position in the room data).</summary>
    public DoorLink? Find(string room, int index)
    {
        _byIndex ??= Links.Values.ToDictionary(l => (l.Door.Room, l.Door.Index));
        return _byIndex.GetValueOrDefault((room, index));
    }
}

/// <summary>
/// Random doors, as BioRand does them: one character plays from the start to the end of the
/// game through 1-4 segments. Each segment but the last ends in a bridge room (the data's
/// <c>segment</c> rooms: the plane, the switch to Chris, the handover), whose one-way story door
/// leads into the next segment; the last ends in the last room of the game. The bridges are
/// used in a random order, and the segment size says how many of the other rooms are used,
/// dealt out over the segments (rooms to save in first, so each segment gets some).
///
/// Rooms joined by doors the data does not let change (<c>randomize: false</c>) or by room
/// requirements go together. Inside a segment its rooms are joined into a tree from the
/// entry, its first door leading to a room to save in; the bridge room (or the end) is joined
/// last, to the room furthest from the entry, and its other doors lead back into it. The
/// doors left are paired with each other (loops), and an odd one out leads back into its own
/// room. Each pair is two-way: door A leads into B's room arriving at B, and door B leads back
/// to A.
///
/// Kept as in the game, inside a segment: one-way and bridge doors, doors with locks, room
/// conditions or flag conditions, and doors into rooms the data excludes. Kept doors that
/// would leave the segment lead back into their own room instead, as do doors into rooms the
/// run does not use.
///
/// Claire's run starts as a new game does, in RM_0000 (the cell, where Rodrigo lets her out);
/// its one door, the first of the game, is the first random door.
/// </summary>
public sealed class DoorRandomizer
{
    public const string StartRoom = "1000";
    public const string ChrisStartRoom = "8180";
    public const int StartEntrance = 0;
    public const string CellRoom = "1000";
    /// <summary>Where the normal game switches to Chris and sends him to <see cref="ChrisStartRoom"/>.</summary>
    public const string SwitchRoom = "70A0";
    public const int MaxSegments = 4;
    public const int MaxSize = 8;

    private readonly RandoData _data;
    private readonly ISet<string> _saveRooms;
    private readonly int _segments;
    private readonly int _size;
    private readonly Character _character;
    private readonly HashSet<string> _skipped;

    /// <param name="saveRooms">Rooms with a typewriter or item box (BioRand ids).</param>
    public DoorRandomizer(RandoData data, ISet<string> saveRooms, RandoSettings settings)
    {
        _data = data;
        _saveRooms = saveRooms;
        _segments = Math.Clamp(settings.DoorSegments, 1, MaxSegments);
        _size = Math.Clamp(settings.DoorSegmentSize, 1, MaxSize);
        _character = settings.DoorCharacter;
        // Chris starts after the switch to him, so the run never goes through it (or Claire's cell).
        _skipped = _character == Character.Chris ? [CellRoom, SwitchRoom] : [];
        EndRoom = data.Map.BeginEndRooms.First(x => x.DoorRando == true).End!;
    }

    public string EndRoom { get; }

    public static string StartOf(Character character) => character == Character.Chris ? ChrisStartRoom : StartRoom;

    public IReadOnlyList<DoorStub> AllStubs() =>
        _data.Map.Rooms.SelectMany(kv => (kv.Value.Doors ?? [])
                .Select((d, i) => (d, i))
                .Where(x => x.d.DoorRando != false && x.d.Target != null && _data.Map.Rooms.ContainsKey(x.d.Target))
                .Select(x => new DoorStub { Room = kv.Key, Index = x.i, Raw = x.d }))
            .ToList();

    /// <summary>A lock the item logic cannot open (always/side/gate...).</summary>
    public static bool IsLocked(MapRoomDoor door) =>
        door.Lock is { } l && !l.Equals("none", StringComparison.OrdinalIgnoreCase) && !l.Equals("unblock", StringComparison.OrdinalIgnoreCase);

    /// <summary>A door that opens from its own side only (the way back is closed until then).</summary>
    public static bool IsUnblock(MapRoomDoor door) =>
        string.Equals(door.Lock, "unblock", StringComparison.OrdinalIgnoreCase);

    private bool HasCategory(string room, string category) =>
        (_data.Map.Rooms[room].DoorRando ?? []).Any(d => string.Equals(d.Category, category, StringComparison.OrdinalIgnoreCase));

    private bool Excluded(string room) => (room == CellRoom && _character == Character.Chris) || HasCategory(room, "exclude");

    /// <summary>Doors that join rooms for good: the data does not let them change, or a room needs another room.</summary>
    private static bool Sticky(DoorStub s) => s.Raw.Randomize == false || s.Raw.RequiresRoom is { Length: > 0 };

    /// <summary>Doors that keep their target (inside a segment).</summary>
    private static bool Special(DoorStub s) =>
        Sticky(s) || s.Raw.NoReturn || s.Raw.IsBridgeEdge || s.Raw.Lock != null || s.Raw.Condition != null || s.Fixed;

    /// <summary>Why the last <see cref="Make"/> returned null.</summary>
    public string? LastFailure { get; private set; }

    private DoorPlan? Fail(string why)
    {
        LastFailure = why;
        return null;
    }

    /// <summary>
    /// The rooms that must be used together, by room: joined by <see cref="Sticky"/> doors and
    /// room requirements (rooms the run never enters left out).
    /// </summary>
    public Dictionary<string, int> Groups(IReadOnlyList<DoorStub> stubs)
    {
        var rooms = _data.Map.Rooms.Keys.Where(r => !_skipped.Contains(r)).Order(StringComparer.Ordinal).ToList();
        var parent = rooms.ToDictionary(r => r, r => r, StringComparer.Ordinal);
        string Find(string r) => parent[r] == r ? r : parent[r] = Find(parent[r]);
        void Join(string a, string b)
        {
            if (parent.ContainsKey(a) && parent.ContainsKey(b))
                parent[Find(a)] = Find(b);
        }

        foreach (var s in stubs.Where(Sticky))
        {
            Join(s.Room, s.OriginalTarget);
            foreach (var r in s.Raw.RequiresRoom ?? [])
                Join(s.Room, r);
        }
        foreach (var (key, room) in _data.Map.Rooms)
        {
            foreach (var r in room.RequiresRoom ?? [])
                Join(key, r);
            foreach (var item in (room.Items ?? []).Where(i => i.DoorRando != false))
            {
                foreach (var r in item.RequiresRoom ?? [])
                    Join(key, r);
            }
        }

        var index = rooms.Select(Find).Distinct().Select((root, i) => (root, i)).ToDictionary(x => x.root, x => x.i);
        return rooms.ToDictionary(r => r, r => index[Find(r)], StringComparer.Ordinal);
    }

    private sealed record Bridge(string Room, DoorStub Door, List<string> Before, List<string> After);

    /// <summary>Makes a door layout, or null when this attempt cannot connect everything.</summary>
    public DoorPlan? Make(Rng rng)
    {
        LastFailure = null;
        var stubs = AllStubs().Where(s => !_skipped.Contains(s.Room)).ToList();
        var groupOf = Groups(stubs);
        var members = groupOf.GroupBy(x => x.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Key).Order(StringComparer.Ordinal).ToList());
        var start = StartOf(_character);
        var startGroup = groupOf[start];
        var endGroup = groupOf[EndRoom];

        // Bridges: a segment room whose one-way door leads to a room the run can use.
        var segmentRooms = groupOf.Keys.Where(r => HasCategory(r, "segment")).Order(StringComparer.Ordinal).ToList();
        var bridges = new List<Bridge>();
        foreach (var room in segmentRooms)
        {
            var door = stubs.FirstOrDefault(s => s.Room == room && s.Raw.IsBridgeEdge && s.Raw.NoReturn);
            if (door == null || _skipped.Contains(door.OriginalTarget) || groupOf[room] == startGroup || groupOf[room] == endGroup)
                continue;
            // The group's rooms on this side of the one-way door, and on the far side.
            var group = members[groupOf[room]];
            var before = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<string>([room]);
            while (stack.Count != 0)
            {
                var r = stack.Pop();
                if (!before.Add(r))
                    continue;
                foreach (var s in stubs.Where(s => Sticky(s) && (s.Room == r || s.OriginalTarget == r)))
                {
                    var other = s.Room == r ? s.OriginalTarget : s.Room;
                    var crossing = (s.Room == room && s.OriginalTarget == door.OriginalTarget) ||
                                   (s.Room == door.OriginalTarget && s.OriginalTarget == room);
                    if (!crossing && group.Contains(other))
                        stack.Push(other);
                }
            }
            var after = group.Where(r => !before.Contains(r)).ToList();
            if (!after.Contains(door.OriginalTarget))
                continue;
            bridges.Add(new Bridge(room, door, before.Order(StringComparer.Ordinal).ToList(), after));
        }

        var count = Math.Min(_segments, bridges.Count + 1);
        var chosen = rng.Shuffle(bridges).Take(count - 1).ToList();

        // The other groups the run may use: not the start's, the end's or a bridge's (an unused
        // bridge is left out whole), with a room that is not excluded, and none of whose doors
        // out of it the engine cannot redirect.
        var reserved = new HashSet<int> { startGroup, endGroup };
        reserved.UnionWith(segmentRooms.Select(r => groupOf[r]));
        var pool = members.Keys.Order()
            .Where(g => !reserved.Contains(g))
            .Where(g => members[g].Any(r => !Excluded(r)))
            .Where(g => !stubs.Any(s => s.Fixed && groupOf[s.Room] == g && (!groupOf.TryGetValue(s.OriginalTarget, out var t) || t != g)))
            .ToList();

        // Deal the groups out (BioRand's GetAreaSuperNodes): rooms to save in first, at least
        // one per segment, then the others; the size keeps a share of each. Save rooms a
        // segment can start with (two doors or more, to go on through) come first, from the
        // first segment on.
        bool GoesOn(int g) => members[g].Any(r => _saveRooms.Contains(r) &&
            stubs.Count(s => s.Room == r && !Special(s) && !Excluded(s.OriginalTarget) &&
                             !stubs.Any(b => b.Room == s.OriginalTarget && b.OriginalTarget == r && Special(b))) >= 2);
        var saveGroups = rng.Shuffle(pool.Where(g => members[g].Any(_saveRooms.Contains))).OrderBy(g => GoesOn(g) ? 0 : 1).ToList();
        var otherGroups = rng.Shuffle(pool.Where(g => !members[g].Any(_saveRooms.Contains)));
        if (_size < MaxSize)
        {
            var saveCount = Math.Min(saveGroups.Count, count + Math.Max(0, saveGroups.Count - count) * (_size - 1) / (MaxSize - 1));
            saveGroups = saveGroups.Take(saveCount).ToList();
            otherGroups = otherGroups.Take(otherGroups.Count * _size / MaxSize).ToList();
        }
        var assigned = Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray();
        var next = 0;
        foreach (var g in saveGroups.Concat(otherGroups))
        {
            assigned[next].Add(g);
            next = (next + 1) % count;
        }

        var links = new Dictionary<DoorStub, DoorLink>();
        var segments = new List<DoorSegment>();
        for (var i = 0; i < count; i++)
        {
            var entry = i == 0 ? start : chosen[i - 1].Door.OriginalTarget;
            var fixedRooms = i == 0 ? members[startGroup] : chosen[i - 1].After;
            var exitRooms = i == count - 1 ? members[endGroup] : chosen[i].Before;
            var exit = i == count - 1 ? EndRoom : chosen[i].Room;

            // When the doors cannot join every group, the groups left over are dropped and
            // the segment is tried again.
            var optional = assigned[i];
            for (var tries = 0; ; tries++)
            {
                var rooms = fixedRooms.Concat(optional.SelectMany(g => members[g])).Concat(exitRooms).ToList();
                var result = GrowSegment(i, entry, rooms, exitRooms.ToHashSet(StringComparer.Ordinal), stubs,
                    i < count - 1 ? chosen[i].Door : null, links, rng);
                if (result.Failure != null)
                    return Fail($"segment {i}: {result.Failure}");
                if (result.Stranded.Count == 0)
                {
                    segments.Add(new DoorSegment(entry, exit, rooms));
                    break;
                }
                var drop = result.Stranded.Select(r => groupOf[r]).ToHashSet();
                if (!drop.IsSubsetOf(optional) || tries == 100)
                    return Fail($"segment {i}: rooms {string.Join(" ", result.Stranded.Order(StringComparer.Ordinal))} cannot be joined");
                foreach (var l in links.Where(l => rooms.Contains(l.Key.Room)).Select(l => l.Key).ToList())
                    links.Remove(l);
                optional = optional.Where(g => !drop.Contains(g)).ToList();
            }
        }

        return new DoorPlan
        {
            Character = _character,
            StartRoom = start,
            StartEntrance = StartEntrance,
            EndRoom = EndRoom,
            Links = links,
            Segments = segments,
        };
    }

    private sealed record GrowResult(string? Failure, HashSet<string> Stranded);

    /// <summary>
    /// Joins the rooms of one segment into a tree from its entry, the exit rooms last, then
    /// pairs the doors left. Returns the rooms it could not reach when the free doors ran out.
    /// </summary>
    private GrowResult GrowSegment(int segIndex, string entry, List<string> segment, HashSet<string> exitRooms,
        IReadOnlyList<DoorStub> stubs, DoorStub? bridgeDoor, Dictionary<DoorStub, DoorLink> links, Rng rng)
    {
        var inSegment = segment.ToHashSet(StringComparer.Ordinal);
        var mine = stubs.Where(s => inSegment.Contains(s.Room)).ToList();

        void Pair(DoorStub a, DoorStub b)
        {
            links[a] = new DoorLink(a, b.Room, b.Arrival, b);
            links[b] = new DoorLink(b, a.Room, a.Arrival, a);
        }
        void DeadEnd(DoorStub s) => links[s] = new DoorLink(s, s.Room, s.Arrival, null);

        // Each door of the segment keeps its target, leads back into its room, or is free.
        // Doors into an excluded room keep it when the room is in the segment; an excluded
        // room the run uses (with its group) has free doors to rooms outside. A door whose way
        // back stays, stays too.
        bool Keeps(DoorStub s) => Special(s) || (Excluded(s.Room) ? inSegment.Contains(s.OriginalTarget) : Excluded(s.OriginalTarget));
        var keepPairs = new HashSet<(string, string)>();
        foreach (var s in mine.Where(s => s != bridgeDoor && Keeps(s) && inSegment.Contains(s.OriginalTarget)))
        {
            keepPairs.Add((s.Room, s.OriginalTarget));
            keepPairs.Add((s.OriginalTarget, s.Room));
        }
        var kept = new HashSet<DoorStub>();
        var free = new List<DoorStub>();
        foreach (var s in mine)
        {
            if (s == bridgeDoor)
                continue;   // on to the next segment
            if (keepPairs.Contains((s.Room, s.OriginalTarget)))
                kept.Add(s);
            else if (!Keeps(s))
                free.Add(s);
            else if (s.Fixed)
                return new GrowResult($"{s} cannot be redirected", []);
            else
                DeadEnd(s);
        }

        // Rooms joined by doors that stay, and that open both ways without keys, are one block.
        // (A locked, one-way or keyed door may not open when the player first meets it: rooms
        // behind it get a random door of their own. Kept doors need their keys from both
        // sides, see ItemGraph.SameKeysBothWays. A door that opens from one side only does not
        // join either: RM_1090's corridor cannot reach its office, see ItemGraph.OneSideOnly.)
        bool Open(DoorStub s) => !s.Raw.NoReturn && !IsLocked(s.Raw) && !IsUnblock(s.Raw) && s.Requires.Length == 0;
        bool Joined(string a, string b) =>
            kept.Any(s => s.Room == a && s.OriginalTarget == b && Open(s)) &&
            kept.Any(s => s.Room == b && s.OriginalTarget == a && Open(s));
        var block = new Dictionary<string, int>();
        var blockCount = 0;
        foreach (var room in segment)
        {
            if (block.ContainsKey(room))
                continue;
            var stack = new Stack<string>([room]);
            while (stack.Count != 0)
            {
                var r = stack.Pop();
                if (!block.TryAdd(r, blockCount))
                    continue;
                foreach (var s in kept.Where(s => s.Room == r && Joined(r, s.OriginalTarget)))
                    stack.Push(s.OriginalTarget);
            }
            blockCount++;
        }
        var exitBlocks = exitRooms.Select(r => block[r]).ToHashSet();
        if (exitBlocks.Contains(block[entry]))
            return new GrowResult("the entry is joined to the exit", []);

        // The exit's doors wait for the end; the tree grows through the others.
        var exitFree = free.Where(s => exitBlocks.Contains(block[s.Room])).ToList();
        var segFree = free.Except(exitFree).ToList();

        var connected = new HashSet<int>();
        var depth = new Dictionary<int, int>();
        var joined = new List<int>();   // blocks joined by a random door, in order
        var open = new List<DoorStub>();
        var unconnected = Enumerable.Range(0, blockCount).Where(b => !exitBlocks.Contains(b))
            .Where(b => segFree.Any(s => block[s.Room] == b)).ToHashSet();

        // A kept door the player can go through from its room, maybe with a key (the item logic
        // checks the keys): not locked, and not the closed side of a one-side door.
        bool Forward(DoorStub s) =>
            !s.Raw.NoReturn && !IsLocked(s.Raw) && kept.Contains(s) &&
            (IsUnblock(s.Raw) || !kept.Any(b => b.Room == s.OriginalTarget && b.OriginalTarget == s.Room && IsUnblock(b.Raw)));
        var forward = segment.ToDictionary(r => r, r => kept.Where(s => s.Room == r && Forward(s)).ToList());

        // A block and the blocks behind its kept doors without keys are reached together.
        void Reach(int b, int d)
        {
            var stack = new Stack<(int Block, int Depth)>([(b, d)]);
            while (stack.Count != 0)
            {
                var (nextBlock, nextDepth) = stack.Pop();
                if (!connected.Add(nextBlock))
                    continue;
                depth[nextBlock] = nextDepth;
                unconnected.Remove(nextBlock);
                if (!exitBlocks.Contains(nextBlock))
                    open.AddRange(segFree.Where(s => block[s.Room] == nextBlock && !links.ContainsKey(s) && !open.Contains(s)));
                foreach (var s in block.Where(x => x.Value == nextBlock).SelectMany(x => forward[x.Key]))
                {
                    if (s.Requires.Length == 0 && !connected.Contains(block[s.OriginalTarget]))
                        stack.Push((block[s.OriginalTarget], nextDepth + 1));
                }
            }
        }

        void Connect(DoorStub from, DoorStub to)
        {
            Pair(from, to);
            open.Remove(from);
            open.Remove(to);
            if (!connected.Contains(block[to.Room]))
            {
                joined.Add(block[to.Room]);
                Reach(block[to.Room], depth[block[from.Room]] + 1);
            }
        }

        Reach(block[entry], 0);

        // The entry's first door leads to a save room of this segment, when it has one (a save
        // room with another door to go on through, so the tree can grow from there).
        var saveStubs = segFree.Where(s => _saveRooms.Contains(s.Room) && !connected.Contains(block[s.Room])
                                           && s.Requires.Length == 0 && segFree.Count(o => block[o.Room] == block[s.Room]) >= 2).ToList();
        var keylessOpen = open.Where(s => s.Requires.Length == 0).ToList();
        var entryDoor = keylessOpen.Where(s => s.Room == entry).ToList();
        if (entryDoor.Count == 0)
            entryDoor = keylessOpen;
        if (saveStubs.Count != 0 && entryDoor.Count != 0)
            Connect(rng.NextOf(entryDoor), rng.NextOf(saveStubs));
        else if (segIndex == 0)
            return new GrowResult("no save room for the start", []);

        // Grow a tree: an open door of the reached rooms to a door of a new block.
        while (unconnected.Count != 0)
        {
            if (open.Count == 0)
            {
                // Out of doors: a block behind a kept door with a key counts as reached.
                var behind = connected.SelectMany(b => block.Where(x => x.Value == b)).SelectMany(x => forward[x.Key])
                    .Where(s => !connected.Contains(block[s.OriginalTarget]))
                    .Select(s => (Block: block[s.OriginalTarget], Depth: depth[block[s.Room]] + 1))
                    .Where(x => !exitBlocks.Contains(x.Block)).Distinct().Order().ToList();
                if (behind.Count == 0)
                    return new GrowResult(null, block.Where(x => unconnected.Contains(x.Value)).Select(x => x.Key).ToHashSet());
                var (b, d) = rng.NextOf(behind);
                Reach(b, d);
                continue;
            }
            var keyless = open.Where(s => s.Requires.Length == 0).ToList();
            var from = keyless.Count != 0 && rng.NextProbability(70) ? rng.NextOf(keyless) : rng.NextOf(open);

            // Keep a door open for the next block and for the exit. The player comes into a new
            // block through a door without keys: its way back is the one the item logic counts on
            // (seed 985463 came into RM_9320 by the open side of its jewel door, and the jewels
            // were behind it). A block with only keyed doors left is stranded.
            var blocksLeft = unconnected.Order().ToList();
            var openAfter = open.Count - 1;
            bool Keyless(DoorStub s) => !links.ContainsKey(s) && s.Requires.Length == 0;
            var candidates = (openAfter == 0
                ? blocksLeft.Where(b => segFree.Count(s => block[s.Room] == b) >= 2)
                : blocksLeft).Where(b => segFree.Any(s => block[s.Room] == b && Keyless(s))).ToList();
            if (candidates.Count == 0)
                break;   // only dead ends left: the last door is the exit's
            var target = rng.NextOf(candidates);
            var to = rng.NextOf(segFree.Where(s => block[s.Room] == target && Keyless(s)).ToList());
            Connect(from, to);
        }

        // Blocks behind kept doors with keys count as reached; the ones the tree could not take
        // are left out (the segment is tried again without them).
        while (true)
        {
            var behind = connected.SelectMany(b => block.Where(x => x.Value == b)).SelectMany(x => forward[x.Key])
                .Where(s => !connected.Contains(block[s.OriginalTarget]) && !exitBlocks.Contains(block[s.OriginalTarget]))
                .Select(s => (Block: block[s.OriginalTarget], Depth: depth[block[s.Room]] + 1)).Distinct().Order().ToList();
            if (behind.Count == 0)
                break;
            Reach(behind[0].Block, behind[0].Depth);
        }
        var stranded = block.Where(x => !connected.Contains(x.Value) && !exitBlocks.Contains(x.Value)).Select(x => x.Key).ToHashSet();

        // The exit: joined to a door of the room furthest from the entry (unless a kept door
        // reached it already); its other doors lead back into it.
        if (!exitBlocks.Any(connected.Contains))
        {
            var exitDoors = exitFree.Where(s => s.Requires.Length == 0).ToList();
            if (exitDoors.Count == 0)
                return new GrowResult("the exit has no free door", []);
            if (open.Count == 0)
            {
                // No door left for it: leave out the last dead end the tree took.
                var leaf = joined.LastOrDefault(b => segFree.Count(s => block[s.Room] == b) == 1, -1);
                return new GrowResult(null, leaf < 0 ? exitRooms : block.Where(x => x.Value == leaf).Select(x => x.Key).ToHashSet());
            }
            var deepest = open.Max(s => depth[block[s.Room]]);
            var from = rng.NextOf(open.Where(s => depth[block[s.Room]] == deepest).ToList());
            var to = rng.NextOf(exitDoors);
            Pair(from, to);
            open.Remove(from);
        }
        foreach (var s in exitFree.Where(s => !links.ContainsKey(s)))
            DeadEnd(s);
        if (stranded.Count != 0)
            return new GrowResult(null, stranded);

        // The rest pair up with each other, each with a door of another room (two doors of one
        // room only lead into each other: seed 117323's RM_7120); one without such a partner is
        // a dead end.
        var rest = rng.Shuffle(segFree.Where(s => !links.ContainsKey(s)));
        while (rest.Count != 0)
        {
            var a = rest[0];
            rest.RemoveAt(0);
            var j = rest.FindIndex(b => b.Room != a.Room);
            if (j < 0)
            {
                DeadEnd(a);
                continue;
            }
            Pair(a, rest[j]);
            rest.RemoveAt(j);
        }
        return new GrowResult(null, []);
    }
}
