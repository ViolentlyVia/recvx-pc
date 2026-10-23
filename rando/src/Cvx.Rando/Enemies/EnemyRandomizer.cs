// Enemy swaps after BioRand's EnemyRandomiser (MIT, Copyright (c) 2022-2026 Ted John): one
// new type per room, weighted by difficulty, at most so many of a type in a room. Unlike
// BioRand's Code: Veronica prototype, which only had stage 1 positions and swapped whole motion
// lists, enemies keep their own positions and only their type, model, motions and textures
// change (see RdxEnemyRewriter). Random placements follow BioRand's too: a share of the rooms
// get enemies, 1 to twice the average less one each, in places worked out from the room
// (EnemySpots), the others none; new enemies get death flags of their own as in its prototype.

using Cvx.Formats;
using Cvx.Formats.Rdx;
using Cvx.Formats.Script;
using Cvx.Rando.Data;
using Cvx.Rando.Items;

namespace Cvx.Rando.Enemies;

/// <summary>What changed in one room, for the spoiler log.</summary>
/// <param name="Count">How many of <paramref name="NewKind"/> the room has now.</param>
/// <param name="SoundFrom">The room whose enemy sound bank the room gets (when all its enemies are the new kind).</param>
/// <param name="Added">Random placements: enemies the room did not have.</param>
/// <param name="Hidden">Random placements: enemies of the room that are gone (counted as killed).</param>
/// <param name="Moved">Random placements: the enemies are in new places.</param>
public sealed record EnemyRoomChange(RoomId Room, string Key, int Count, IReadOnlyList<int> OldTypes, EnemyKind NewKind,
    IReadOnlyList<RoomId> Donors, RoomId? SoundFrom, int Added = 0, int Hidden = 0, bool Moved = false);

public sealed class EnemyRandomizer
{
    /// <summary>Room memory kept free above the expanded room, besides what the check measures.</summary>
    public const int MemoryMargin = 64 * 1024;
    /// <summary>More room memory kept free for each added enemy (its work: matrices, motion state).</summary>
    public const int MemoryPerEnemy = 32 * 1024;
    /// <summary>Random placements: the most enemies placed in one room.</summary>
    public const int MaxPlaced = 16;
    /// <summary>Random placements: how far apart placed enemies start (then half that when the room is small).</summary>
    public const float Spacing = 10f;
    private const int Bat = 7;

    // How often each kind difficulty (columns 0-3) comes up at each setting (rows 0-3).
    private static readonly double[][] Weights =
    [
        [4, 3, 1, 0.25],
        [2, 3, 2, 1],
        [1, 2, 3, 2],
        [0.5, 1, 3, 4],
    ];

    private readonly RandoData _data;
    private readonly RoomArchive _archive;
    private readonly RandoSettings _settings;
    private readonly Rng _rng;
    private readonly Rng _placeRng;
    private readonly Dictionary<string, RdxEnemyDonor> _donors = [];
    private readonly int _difficulty;

    // Death flags for new enemies: each room number (all versions of a room) its own, in order;
    // when the free ones run out they are shared, as BioRand does.
    private readonly Dictionary<(int Stage, int Room), List<int>> _roomFlags = [];
    private readonly Queue<int> _freeFlags = new();
    private readonly List<int> _givenFlags = [];
    private int _reused;

    /// <param name="placeRng">Random placements' own stream (rooms, counts, places).</param>
    public EnemyRandomizer(RandoData data, RoomArchive archive, RandoSettings settings, Rng rng, Rng? placeRng = null)
    {
        _data = data;
        _archive = archive;
        _settings = settings;
        _rng = rng;
        _placeRng = placeRng ?? rng;
        _difficulty = Math.Clamp(settings.EnemyDifficulty, 0, 3);
    }

    /// <summary>Random placements: rooms whose enemies all went (or that had none and got none).</summary>
    public List<RoomId> Cleared { get; } = [];

    /// <summary>Random placements: new enemies that share a death flag with another room's (the free flags ran out).</summary>
    public int SharedFlags => _reused;

    private sealed record Room(string Key, RoomId Id, MapRoomEnemies? Spec, RdxFile Original, RdxEnemyLayout Layout,
        IReadOnlyList<CvOpcode> Opcodes, List<RdxEnemyAssets> Targets);

    /// <summary>Picks new enemies for every room that allows it and queues the room rebuilds.</summary>
    public List<EnemyRoomChange> Apply(ItemPatcher rooms)
    {
        var catalog = _data.Enemies;
        var scripted = catalog.ScriptedOpcodes.ToHashSet();
        var all = new List<Room>();
        // (the rooms read in parallel: expanding them is the slow part)
        var keys = _data.Map.Rooms.Keys.Where(k => RoomId.TryParseBioRand(k, out var r) && RoomTable.Contains(r)).ToArray();
        var files = new RdxFile[keys.Length];
        Parallel.For(0, keys.Length, i => files[i] = _archive.ReadRoom(RoomId.ParseBioRand(keys[i])));
        var read = keys.Zip(files).ToDictionary(x => x.First, x => x.Second);
        foreach (var (key, map) in _data.Map.Rooms.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (catalog.KeepRooms.Contains(key))
                continue;
            var spec = (map.Enemies ?? []).FirstOrDefault(s => s.DoorRando == null || s.DoorRando == _settings.RandomDoors);
            if (spec?.Restricted == true)
                continue;
            var id = RoomId.ParseBioRand(key);
            if (!RoomTable.Contains(id))
                continue;

            var original = read[key];
            var layout = RdxEnemyLayout.Read(original);
            if (!layout.IsConsistent || layout.Enemies.Any(e => !e.HasModel))
                continue;
            var opcodes = CvScriptReader.ReadOpcodes(original).ToList();
            if (opcodes.Any(o => scripted.Contains(o.Name)))
                continue;
            var targets = layout.Enemies.Where(e => catalog.Replaceable.Contains(e.Type)).ToList();
            // Enemies an event moves or animates keep their type (the event's motions are theirs).
            var controlled = opcodes.Where(o => catalog.WorkOpcodes.Contains(o.Name) && o.Bytes.Length >= 3 && o.Bytes.Span[1] == 1)
                .Select(o => (int)o.Bytes.Span[2]).ToHashSet();
            targets.RemoveAll(t => controlled.Contains(t.Index));
            all.Add(new Room(key, id, spec, original, layout, opcodes, targets));
        }

        var changes = new List<EnemyRoomChange>();
        var placing = _settings.RandomEnemyPlacements ? Placements(all) : [];
        foreach (var room in all)
        {
            EnemyRoomChange? change;
            if (placing.TryGetValue(room.Key, out var spots))
            {
                change = spots == null ? Clear(rooms, room) : Populate(rooms, room, spots);
                if (change == null && spots != null && room.Targets.Count != 0)
                    change = Swap(rooms, room);
            }
            else
            {
                change = room.Targets.Count != 0 ? Swap(rooms, room) : null;
            }
            if (change != null)
                changes.Add(change);
        }
        return changes;
    }

    /// <summary>Swaps a room's enemies for another kind in their own places.</summary>
    private EnemyRoomChange? Swap(ItemPatcher rooms, Room room)
    {
        var targets = room.Targets;
        var candidates = Kinds(room.Spec).Where(k => k.MaxAt(_difficulty) >= targets.Count).ToList();
        // A room with more enemies than any kind may hold at this difficulty keeps them, unless
        // they are a kind set to None (RM_0130's five bats at Easy): then it changes all the same
        // (and keeps its count).
        bool AtNone(RdxEnemyAssets t) =>
            _data.Enemies.Types.Where(k => k.Type == t.Type && (k.Version == null || k.Version == t.ModelVersion)).ToList() is { Count: > 0 } own &&
            own.All(k => Weight(k) == 0);
        var fallback = targets.Any(AtNone) ? Kinds(room.Spec).Where(k => k.MaxAt(_difficulty) < targets.Count).ToList() : [];

        // Try kinds by weight until one fits the room.
        while (candidates.Count != 0 || fallback.Count != 0)
        {
            if (candidates.Count == 0)
                (candidates, fallback) = (fallback, []);
            var kind = _rng.NextWeighted(candidates.Select(k => (k, Weight(k))).ToList());
            candidates.Remove(kind);
            var plan = Plan(kind, targets.Count);
            if (plan == null)
                continue;
            var replacements = targets.Select((t, i) => (t.Index, plan[i])).ToDictionary(x => x.Index, x => x.Item2);
            Func<RdxFile, RdxFile> build = file => RdxEnemyRewriter.Replace(file, replacements);
            // Bats hang from the ceiling: what takes their place starts on the floor below them.
            if (kind.Type != Bat && targets.Any(t => t.Type == Bat))
            {
                if (FloorPlaces(room, targets.Where(t => t.Type == Bat).ToList(), kind) is not { } floor)
                    continue;
                var slots = room.Layout.Enemies.Select(e => new RdxEnemySlot(e.Index, replacements.GetValueOrDefault(e.Index),
                    floor.TryGetValue(e.Index, out var p) ? p : null)).ToList();
                build = file => RdxEnemyRewriter.Rebuild(file, slots);
            }
            if (Rebuild(rooms, room.Id, build, 0) is not { } rebuilt)
                continue;

            foreach (var nop in room.Spec?.Nop ?? [])
                rooms.Nop(room.Id, nop);
            rooms.SetRebuild(room.Id, build);
            return Change(room, rebuilt, kind, targets.Count, plan);
        }
        return null;
    }

    /// <summary>
    /// For each bat, the place on the floor nearest below it where <paramref name="kind"/> fits
    /// (spread out), or null when the room has too few.
    /// </summary>
    private Dictionary<int, RdxVectorF>? FloorPlaces(Room room, List<RdxEnemyAssets> bats, EnemyKind kind)
    {
        var free = EnemySpots.Find(room.Original, EnemySpots.Known(room.Id, _archive, _data)).Where(s => s.Fits(kind.Radius)).ToList();
        var result = new Dictionary<int, RdxVectorF>();
        foreach (var bat in bats)
        {
            var at = bat.Entry.Position;
            var spot = free.Where(s => s.Position.Y <= at.Y && result.Values.All(r => Distance(r, s.Position) >= Spacing / 2))
                .OrderBy(s => Distance(s.Position, at)).ThenBy(s => at.Y - s.Position.Y).FirstOrDefault();
            if (spot == null)
                return null;
            result[bat.Index] = spot.Position;
        }
        return result;
    }

    /// <summary>
    /// Which rooms random placements decide (BioRand ids), and for each the places its enemies
    /// can start (null: the room gets none).
    /// </summary>
    private Dictionary<string, IReadOnlyList<EnemySpots.Spot>?> Placements(List<Room> all)
    {
        var catalog = _data.Enemies;
        var saveRooms = Generator.SaveRoomKeys(_data);
        var eligible = all.Where(r =>
            r.Spec?.KeepPositions != true &&
            (_settings.EnemiesInSaveRooms || !saveRooms.Contains(r.Key)) &&
            r.Original.Script.Count >= 2 &&
            // no boss or other special enemy (types up to 40 are enemies, above characters and
            // props); bats are ordinary
            !r.Layout.Enemies.Any(e => e.Type is > 0 and <= 40 && !catalog.Replaceable.Contains(e.Type) && e.Type != Bat) &&
            // a room with characters for its cutscenes only gets enemies where it had some
            (r.Targets.Count != 0 || !r.Layout.Enemies.Any(e => e.Type > 90))).ToList();

        // Where enemies can start (the slow part: rooms in parallel).
        var spots = new IReadOnlyList<EnemySpots.Spot>[eligible.Count];
        Parallel.For(0, eligible.Count, i =>
            spots[i] = EnemySpots.Find(eligible[i].Original, EnemySpots.Known(eligible[i].Id, _archive, _data)));
        var withSpots = eligible.Select((r, i) => (Room: r, Spots: spots[i])).Where(x => x.Spots.Count != 0).ToList();

        // A share of them get enemies (BioRand: rooms with enemies, in sevenths).
        var populated = (int)(Math.Clamp(_settings.EnemyRooms, 0, 7) / 7.0 * withSpots.Count);
        var order = _placeRng.Shuffle(withSpots);
        var result = new Dictionary<string, IReadOnlyList<EnemySpots.Spot>?>();
        for (var i = 0; i < order.Count; i++)
            result[order[i].Room.Key] = i < populated ? order[i].Spots : null;

        var flags = EnemyFlags.Of(_archive);
        foreach (var f in Enumerable.Range(0, EnemyFlags.Count).Where(f => !flags.Used.Contains(f)))
            _freeFlags.Enqueue(f);
        return result;
    }

    /// <summary>
    /// Random placements in a room: its enemies (those the randomizer may change) move to new
    /// places and become one kind, more come, or some go: they are counted as killed (their death
    /// flag is set as the room starts, so its own <c>set_ck_enemy</c> hides them). Enemies are
    /// only added at the end of the room's list, so the scripts' enemy numbers stay right.
    /// </summary>
    private EnemyRoomChange? Populate(ItemPatcher rooms, Room room, IReadOnlyList<EnemySpots.Spot> spots)
    {
        var (keep, hide) = Hideable(room);
        var average = Math.Clamp(_settings.EnemyQuantity, 0, 15) + 1;
        var candidates = Kinds(room.Spec)
            .Where(k => k.MaxAt(_difficulty) >= keep.Count && spots.Count(s => s.Fits(k.Radius)) >= Math.Max(keep.Count, 1))
            .ToList();
        while (candidates.Count != 0)
        {
            var kind = _rng.NextWeighted(candidates.Select(k => (k, Weight(k))).ToList());
            candidates.Remove(kind);

            var wanted = Math.Min(Math.Min(_placeRng.Next(1, average * 2), kind.MaxAt(_difficulty)), MaxPlaced);
            var places = Pick(spots, Math.Max(wanted, keep.Count), kind.Radius);
            var count = Math.Max(keep.Count, Math.Min(wanted, places.Count));
            if (places.Count < count)
                continue;

            // The room's enemies first (those that cannot go, then the others), then new ones.
            var moved = keep.Concat(hide).Take(count).ToList();
            var gone = hide.Where(h => !moved.Contains(h)).ToList();
            for (var added = count - moved.Count; added >= 0; added--)
            {
                var total = moved.Count + added;
                // (the ones that go become the kind too, so the room's enemy sounds can follow)
                var plan = Plan(kind, total + gone.Count);
                if (plan == null)
                    break;
                var slots = room.Layout.Enemies.Select(e => new RdxEnemySlot(e.Index)).ToList();
                for (var i = 0; i < moved.Count; i++)
                    slots[moved[i].Index] = new RdxEnemySlot(moved[i].Index, plan[i], places[i].Position, Direction());
                for (var i = 0; i < gone.Count; i++)
                    slots[gone[i].Index] = new RdxEnemySlot(gone[i].Index, plan[total + i]);
                var flags = FlagsFor(room.Id, added);
                var start = new List<byte>();
                var frame = new List<byte>();
                foreach (var g in gone)
                    start.AddRange(EnemyFlags.MarkDead(FlagOf(room, g.Index)));
                for (var i = 0; i < added; i++)
                {
                    var index = slots.Count;
                    slots.Add(new RdxEnemySlot(null, plan[moved.Count + i], places[moved.Count + i].Position, Direction()));
                    start.AddRange(EnemyFlags.HideIfDead(index, flags[i]));
                    frame.AddRange(EnemyFlags.MarkDeath(index, flags[i]));
                }
                RdxFile Build(RdxFile file) => Prepend(RdxEnemyRewriter.Rebuild(file, slots), start, frame);
                if (Rebuild(rooms, room.Id, Build, added) is not { } rebuilt)
                    continue;

                foreach (var nop in room.Spec?.Nop ?? [])
                    rooms.Nop(room.Id, nop);
                rooms.SetRebuild(room.Id, Build);
                return Change(room, rebuilt, kind, total, plan) with { Added = added, Hidden = gone.Count, Moved = true };
            }
        }
        return null;
    }

    /// <summary>Random placements, a room without enemies: the ones it had are counted as killed.</summary>
    private EnemyRoomChange? Clear(ItemPatcher rooms, Room room)
    {
        var (keep, hide) = Hideable(room);
        if (hide.Count != 0)
        {
            var start = hide.SelectMany(h => EnemyFlags.MarkDead(FlagOf(room, h.Index))).ToList();
            rooms.SetRebuild(room.Id, file => Prepend(file, start, []));
        }
        if (keep.Count == 0)
            Cleared.Add(room.Id);
        return null;
    }

    /// <summary>
    /// The room's enemies the randomizer may change, split into those that can be counted as
    /// killed (they have a death flag only their room reads) and those that cannot.
    /// </summary>
    private (List<RdxEnemyAssets> Keep, List<RdxEnemyAssets> Hide) Hideable(Room room)
    {
        var flags = EnemyFlags.ByEnemy(room.Opcodes);
        var story = EnemyFlags.Of(_archive).Story;
        var hide = room.Targets.Where(t => flags.TryGetValue(t.Index, out var f) && !story.Contains(f)).ToList();
        return (room.Targets.Except(hide).ToList(), hide);
    }

    private static int FlagOf(Room room, int enemy) => EnemyFlags.ByEnemy(room.Opcodes)[enemy];

    /// <summary>Up to <paramref name="count"/> places an enemy of <paramref name="radius"/> fits, spread out.</summary>
    private List<EnemySpots.Spot> Pick(IReadOnlyList<EnemySpots.Spot> spots, int count, float radius)
    {
        var fitting = _placeRng.Shuffle(spots.Where(s => s.Fits(radius)));
        var result = new List<EnemySpots.Spot>();
        foreach (var spacing in new[] { Spacing, Spacing / 2, 0 })
        {
            foreach (var s in fitting)
            {
                if (result.Count >= count)
                    return result;
                if (!result.Contains(s) && result.All(r => Distance(r.Position, s.Position) >= spacing))
                    result.Add(s);
            }
        }
        return result;
    }

    private int Direction() => _placeRng.Next(-32768, 32768);

    /// <summary>
    /// The death flags of a room's first <paramref name="count"/> new enemies: the room number's
    /// own (its versions share them), taken from the free ones the first time.
    /// </summary>
    private List<int> FlagsFor(RoomId id, int count)
    {
        if (!_roomFlags.TryGetValue((id.Stage, id.Room), out var list))
            _roomFlags[(id.Stage, id.Room)] = list = [];
        while (list.Count < count)
        {
            if (_freeFlags.Count != 0)
            {
                var flag = _freeFlags.Dequeue();
                _givenFlags.Add(flag);
                list.Add(flag);
            }
            else
            {
                list.Add(_givenFlags[_reused++ % _givenFlags.Count]);
            }
        }
        return list.Take(count).ToList();
    }

    /// <summary>
    /// The room rebuilt by <paramref name="build"/> on top of every change so far, or null when it
    /// cannot be (texture slots clash, or it would not fit the room memory).
    /// </summary>
    private static RdxFile? Rebuild(ItemPatcher rooms, RoomId id, Func<RdxFile, RdxFile> build, int added)
    {
        try
        {
            var current = rooms.Current(id);
            if (!RdxEnemyLayout.Read(current).IsConsistent)
                throw new InvalidDataException($"{id}: enemy data not understood after earlier changes");
            var rebuilt = build(current);
            return rebuilt.Length > Expand.RoomPool - MemoryMargin - added * MemoryPerEnemy ? null : rebuilt;
        }
        catch (RdxEnemyRewriter.ConflictException)
        {
            return null;
        }
    }

    /// <summary>Puts opcodes first in the room's start procedure (0) and its every-frame one (1).</summary>
    private static RdxFile Prepend(RdxFile file, IReadOnlyCollection<byte> start, IReadOnlyCollection<byte> frame)
    {
        if (start.Count == 0 && frame.Count == 0)
            return file;
        var builder = file.ToBuilder();
        var script = builder.Script;
        var procedures = Enumerable.Range(0, script.Count).Select(i => script[i]).ToList();
        procedures[0] = start.Concat(procedures[0].ToArray()).ToArray();
        procedures[1] = frame.Concat(procedures[1].ToArray()).ToArray();
        builder.Script = RdxScript.FromProcedures(procedures);
        return builder.ToFile();
    }

    private EnemyRoomChange Change(Room room, RdxFile rebuilt, EnemyKind kind, int count, List<RdxEnemyDonor> plan)
    {
        // The room's enemy sounds (sound pack bank 3) can follow when no other enemy needs them.
        var others = RdxEnemyLayout.Read(rebuilt).Enemies.Any(e => e.Type is > 0 and <= 30 && e.Type != kind.Type);
        var donorRooms = plan.Select(d => d.Room).Distinct().ToList();
        return new EnemyRoomChange(room.Id, room.Key, count, room.Targets.Select(t => t.Type).ToList(), kind,
            donorRooms, others ? null : donorRooms[0]);
    }

    /// <summary>The kinds the settings allow in a room.</summary>
    private List<EnemyKind> Kinds(MapRoomEnemies? spec)
    {
        var allowed = _data.Enemies.Types
            .Where(k => Weight(k) > 0)
            .Where(k => spec?.IncludeTypes is not { Length: > 0 } inc || inc.Contains(k.Type))
            .Where(k => spec?.ExcludeTypes is not { } exc || !exc.Contains(k.Type))
            .ToList();
        // a room for easy kinds only (RM_0130: bats) takes the easiest of those left when its own are at None
        var maxKind = Math.Min(spec?.MaxDifficulty ?? 3, 3);
        if (allowed.Count != 0 && allowed.All(k => k.Difficulty > maxKind))
            maxKind = allowed.Min(k => k.Difficulty);
        return allowed.Where(k => k.Difficulty <= maxKind).ToList();
    }

    /// <summary>
    /// How likely a kind is: the settings' ratio (BioRand's sliders), or without ratios the
    /// difficulty's own mix (untested kinds never).
    /// </summary>
    private double Weight(EnemyKind kind) =>
        _settings.EnemyRatios.Count != 0
            ? kind.RatioIn(_settings)
            : kind.Untested ? 0 : Weights[_difficulty][kind.Difficulty] * kind.Weight;

    /// <summary>A donor for each of <paramref name="count"/> enemies: one per kind, or one or two zombie skins per room.</summary>
    private List<RdxEnemyDonor>? Plan(EnemyKind kind, int count)
    {
        var donors = _rng.Shuffle(kind.Donors).Take(kind.Donors.Length > 1 && count > 2 ? 2 : 1)
            .Select(d => Donor(kind, d)).ToList();
        if (donors.Any(d => d == null))
            return null;
        return Enumerable.Range(0, count).Select(i => donors[i % donors.Count]!).ToList();
    }

    private RdxEnemyDonor? Donor(EnemyKind kind, string spec)
    {
        if (_donors.TryGetValue(spec, out var donor))
            return donor;
        var parts = spec.Split('#');
        var room = RoomId.ParseName(parts[0]);
        donor = RdxEnemyDonor.From(room, _archive.ReadRoom(room), int.Parse(parts[1]), kind.Effect)
            ?? throw new InvalidDataException($"enemies.json: {spec} ({kind.Name}) cannot be copied.");
        if (donor.Type != kind.Type || (kind.Version is int v && donor.ModelVersion != v))
            throw new InvalidDataException($"enemies.json: {spec} is type {donor.Type} version {donor.ModelVersion}, not {kind.Name}.");
        return _donors[spec] = donor;
    }

    private static float Distance(RdxVectorF a, RdxVectorF b) =>
        MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
}
