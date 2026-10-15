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

    public override string ToString() => $"{Room}#{Raw.Id?.ToString() ?? "-"}->{OriginalTarget}";
}

/// <summary>Where a door leads after randomizing (to its own room when it is a dead end).</summary>
public sealed record DoorLink(DoorStub Door, string TargetRoom, int Arrival, DoorStub? Partner);

/// <summary>The door layout of a seed.</summary>
public sealed class DoorPlan
{
    public required string StartRoom { get; init; }
    public required int StartEntrance { get; init; }
    public required IReadOnlyDictionary<DoorStub, DoorLink> Links { get; init; }
    public required IReadOnlyList<IReadOnlyList<string>> Segments { get; init; }

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
/// Random doors: inside each part of the game between one-way doors (Rockfort, the Antarctic
/// base..., Chris), the doors of the rooms the data allows are paired up at random. Each pair
/// is two-way: door A leads into B's room arriving at B, and door B leads back to A. Rooms are
/// joined into a tree from the start so all of them are reachable; the remaining doors are
/// paired with each other (loops) and an odd one out leads back into its own room.
///
/// Kept as in the game: doors into or out of rooms the data excludes, one-way and bridge doors,
/// doors with locks, room conditions or flag conditions (and the doors that lead back through
/// them), and doors the data marks <c>randomize: false</c>.
///
/// A new game starts in <see cref="StartRoom"/> (RM_0010, skipping the cell); its door to the
/// cell leads back into the room, and its other door leads to a save room.
/// </summary>
public sealed class DoorRandomizer
{
    public const string StartRoom = "1010";
    public const int StartEntrance = 0;
    public const string CellRoom = "1000";

    private readonly RandoData _data;
    private readonly ISet<string> _saveRooms;
    private readonly bool _includeChris;

    /// <param name="saveRooms">Rooms with a typewriter or item box (BioRand ids).</param>
    /// <param name="includeChris">Also randomize the doors of Chris's part.</param>
    public DoorRandomizer(RandoData data, ISet<string> saveRooms, bool includeChris)
    {
        _data = data;
        _saveRooms = saveRooms;
        _includeChris = includeChris;
    }

    public IReadOnlyList<DoorStub> AllStubs() =>
        _data.Map.Rooms.SelectMany(kv => (kv.Value.Doors ?? [])
                .Select((d, i) => (d, i))
                .Where(x => x.d.DoorRando != false && x.d.Target != null && _data.Map.Rooms.ContainsKey(x.d.Target))
                .Select(x => new DoorStub { Room = kv.Key, Index = x.i, Raw = x.d }))
            .ToList();

    /// <summary>A lock the item logic cannot open (always/side/gate...).</summary>
    public static bool IsLocked(MapRoomDoor door) =>
        door.Lock is { } l && !l.Equals("none", StringComparison.OrdinalIgnoreCase) && !l.Equals("unblock", StringComparison.OrdinalIgnoreCase);

    private bool Excluded(string room) =>
        room == CellRoom ||
        (_data.Map.Rooms[room].DoorRando ?? []).Any(d => string.Equals(d.Category, "exclude", StringComparison.OrdinalIgnoreCase));

    /// <summary>Why the last <see cref="Make"/> returned null.</summary>
    public string? LastFailure { get; private set; }

    private DoorPlan? Fail(string why)
    {
        LastFailure = why;
        return null;
    }

    /// <summary>Makes a door layout, or null when this attempt cannot connect everything.</summary>
    public DoorPlan? Make(Rng rng)
    {
        LastFailure = null;
        var stubs = AllStubs();
        var segments = Segments(stubs);
        var segmentOf = segments.SelectMany((s, i) => s.Select(r => (r, i))).ToDictionary(x => x.r, x => x.i);

        // Doors that stay as they are.
        bool Special(DoorStub s) =>
            s.Raw.Randomize == false || s.Raw.NoReturn || s.Raw.IsBridgeEdge || s.Raw.Lock != null ||
            s.Raw.RequiresRoom is { Length: > 0 } || s.Raw.Condition != null || s.Raw.Id == null && s.Raw.Offsets == null;
        var fixedPairs = new HashSet<(string, string)>();
        foreach (var s in stubs)
        {
            var sameSegment = segmentOf.TryGetValue(s.Room, out var a) && segmentOf.TryGetValue(s.OriginalTarget, out var b) && a == b;
            if (Special(s) || Excluded(s.Room) || Excluded(s.OriginalTarget) || !sameSegment)
            {
                fixedPairs.Add((s.Room, s.OriginalTarget));
                fixedPairs.Add((s.OriginalTarget, s.Room));
            }
        }

        var links = new Dictionary<DoorStub, DoorLink>();
        void DeadEnd(DoorStub s) => links[s] = new DoorLink(s, s.Room, s.Arrival, null);

        // The start: the door back to the cell is a dead end.
        foreach (var s in stubs.Where(s => s.Room == StartRoom && s.OriginalTarget == CellRoom))
            DeadEnd(s);

        for (var segIndex = 0; segIndex < segments.Count; segIndex++)
        {
            var segment = segments[segIndex];
            if (!_includeChris && RoomId.ParseBioRand(segment[0]).Stage == 9)
                continue;   // Chris's part keeps its doors

            // When the free doors cannot join every room of the part, the rooms left over
            // keep their own doors and the part is tried again.
            for (var tries = 0; ; tries++)
            {
                var segFree = stubs.Where(s => segmentOf.TryGetValue(s.Room, out var si) && si == segIndex && !fixedPairs.Contains((s.Room, s.OriginalTarget))).ToList();
                var result = GrowSegment(segIndex, segment, segFree, stubs, fixedPairs, segmentOf, links, rng);
                if (result.SaveRoomMissing)
                    return Fail($"segment {segIndex}: no save room for the start");
                if (result.Stranded.Count == 0)
                    break;
                foreach (var s in segFree)
                    links.Remove(s);
                if (tries == 100)
                    return Fail($"segment {segIndex}: rooms {string.Join(" ", result.Stranded)} cannot be joined");
                // Their doors and the doors that lead into them keep their targets. A room left
                // with no free door by that keeps its doors too, and the doors into it (the
                // data's doors are not always in pairs: RM_4001 is entered from RM_4010 but its
                // door leads to RM_4011).
                var keep = new HashSet<string>(result.Stranded);
                var hadFree = segFree.Select(s => s.Room).ToHashSet();
                while (true)
                {
                    foreach (var s in segFree.Where(s => keep.Contains(s.Room) || keep.Contains(s.OriginalTarget)))
                    {
                        fixedPairs.Add((s.Room, s.OriginalTarget));
                        fixedPairs.Add((s.OriginalTarget, s.Room));
                    }
                    segFree = segFree.Where(s => !fixedPairs.Contains((s.Room, s.OriginalTarget))).ToList();
                    var closed = hadFree.Where(r => !keep.Contains(r) && segFree.All(s => s.Room != r)).ToList();
                    if (closed.Count == 0)
                        break;
                    keep.UnionWith(closed);
                }
            }
        }

        return new DoorPlan
        {
            StartRoom = StartRoom,
            StartEntrance = StartEntrance,
            Links = links,
            Segments = segments,
        };
    }

    private sealed record GrowResult(bool SaveRoomMissing, HashSet<string> Stranded);

    /// <summary>
    /// Joins the rooms of one part into a tree from its entry, then pairs the doors left. Returns
    /// the rooms it could not reach when the free doors ran out.
    /// </summary>
    private GrowResult GrowSegment(int segIndex, List<string> segment, List<DoorStub> segFree, IReadOnlyList<DoorStub> stubs,
        HashSet<(string, string)> fixedPairs, Dictionary<string, int> segmentOf, Dictionary<DoorStub, DoorLink> links, Rng rng)
    {
        void Pair(DoorStub a, DoorStub b)
        {
            links[a] = new DoorLink(a, b.Room, b.Arrival, b);
            links[b] = new DoorLink(b, a.Room, a.Arrival, a);
        }

        // Rooms joined by doors that stay, and that open both ways without keys, are one block.
        // (A locked, one-way or keyed door may not open when the player first meets it: rooms
        // behind it get a random door of their own. Kept doors need their keys from both
        // sides, see ItemGraph.SameKeysBothWays.)
        bool Open(DoorStub s) => !s.Raw.NoReturn && !IsLocked(s.Raw) && s.Requires.Length == 0;
        bool Joined(string a, string b) =>
            stubs.Any(s => s.Room == a && s.OriginalTarget == b && Open(s)) &&
            stubs.Any(s => s.Room == b && s.OriginalTarget == a && Open(s));
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
                foreach (var s in stubs.Where(s => s.Room == r && fixedPairs.Contains((s.Room, s.OriginalTarget))))
                {
                    if (segmentOf.TryGetValue(s.OriginalTarget, out var t) && t == segIndex && Joined(r, s.OriginalTarget))
                        stack.Push(s.OriginalTarget);
                }
            }
            blockCount++;
        }

        var entry = segIndex == 0 ? StartRoom : segment[0];
        var connected = new HashSet<int> { block[entry] };
        var open = segFree.Where(s => connected.Contains(block[s.Room])).ToList();
        var unconnected = Enumerable.Range(0, blockCount).Where(b => !connected.Contains(b))
            .Where(b => segFree.Any(s => block[s.Room] == b)).ToHashSet();

        void Connect(DoorStub from, DoorStub to)
        {
            Pair(from, to);
            open.Remove(from);
            open.Remove(to);
            var b = block[to.Room];
            if (connected.Add(b))
            {
                unconnected.Remove(b);
                open.AddRange(segFree.Where(s => block[s.Room] == b && !links.ContainsKey(s)));
            }
        }

        // The entry's first door leads to a save room of this part, when it has one (a save
        // room with another door to go on through, so the tree can grow from there).
        var saveStubs = segFree.Where(s => _saveRooms.Contains(s.Room) && s.Room != entry && !connected.Contains(block[s.Room])
                                           && s.Requires.Length == 0 && segFree.Count(o => block[o.Room] == block[s.Room]) >= 2).ToList();
        var entryDoor = open.Where(s => s.Room == entry && s.Requires.Length == 0).ToList();
        if (saveStubs.Count != 0 && entryDoor.Count != 0)
            Connect(rng.NextOf(entryDoor), rng.NextOf(saveStubs));
        else if (segIndex == 0)
            return new GrowResult(true, []);

        // Grow a tree: an open door of the reached rooms to a door of a new block.
        while (unconnected.Count != 0)
        {
            if (open.Count == 0)
                return new GrowResult(false, block.Where(x => unconnected.Contains(x.Value)).Select(x => x.Key).ToHashSet());
            var keyless = open.Where(s => s.Requires.Length == 0).ToList();
            var from = keyless.Count != 0 && rng.NextProbability(70) ? rng.NextOf(keyless) : rng.NextOf(open);

            var blocksLeft = unconnected.ToList();
            var openAfter = open.Count - 1;
            List<int> candidates = openAfter == 0
                ? blocksLeft.Where(b => segFree.Count(s => block[s.Room] == b) >= 2).ToList()
                : blocksLeft;
            if (candidates.Count == 0)
                candidates = blocksLeft;
            var target = rng.NextOf(candidates.Order().ToList());
            var to = rng.NextOf(segFree.Where(s => block[s.Room] == target && !links.ContainsKey(s)).ToList());
            Connect(from, to);
        }

        // The rest pair up with each other; an odd one out is a dead end.
        var rest = rng.Shuffle(segFree.Where(s => !links.ContainsKey(s)));
        for (var i = 0; i + 1 < rest.Count; i += 2)
            Pair(rest[i], rest[i + 1]);
        if (rest.Count % 2 == 1)
            links[rest[^1]] = new DoorLink(rest[^1], rest[^1].Room, rest[^1].Arrival, null);
        return new GrowResult(false, []);
    }

    /// <summary>
    /// The parts of the game between one-way doors, in play order: everything reachable from
    /// the start without a one-way door, then from each one-way door's target.
    /// </summary>
    public List<List<string>> Segments(IReadOnlyList<DoorStub> stubs)
    {
        var result = new List<List<string>>();
        var seen = new HashSet<string> { CellRoom };
        var queue = new Queue<string>([StartRoom]);
        while (queue.Count != 0)
        {
            var entry = queue.Dequeue();
            if (seen.Contains(entry))
                continue;
            var segment = new List<string>();
            var stack = new Stack<string>([entry]);
            var exits = new List<string>();
            while (stack.Count != 0)
            {
                var r = stack.Pop();
                if (!seen.Add(r))
                    continue;
                segment.Add(r);
                foreach (var s in stubs.Where(s => s.Room == r))
                {
                    if (s.Raw.NoReturn)
                        exits.Add(s.OriginalTarget);
                    else
                        stack.Push(s.OriginalTarget);
                }
            }
            // keep the entry first, the rest sorted
            result.Add([entry, .. segment.Where(x => x != entry).Order(StringComparer.Ordinal)]);
            foreach (var e in exits.Order(StringComparer.Ordinal))
                queue.Enqueue(e);
        }
        return result;
    }
}
