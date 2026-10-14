// Enemy swaps after BioRand's EnemyRandomiser (MIT, Copyright (c) 2022-2026 Ted John): one
// new type per room, weighted by difficulty, at most so many of a type in a room. Unlike
// BioRand's Code: Veronica prototype, which only had stage 1 positions and swapped whole motion
// lists, enemies keep their own positions and only their type, model, motions and textures
// change (see RdxEnemyRewriter).

using Cvx.Formats;
using Cvx.Formats.Rdx;
using Cvx.Formats.Script;
using Cvx.Rando.Data;
using Cvx.Rando.Items;

namespace Cvx.Rando.Enemies;

/// <summary>What changed in one room, for the spoiler log.</summary>
/// <param name="SoundFrom">The room whose enemy sound bank the room gets (when all its enemies are the new kind).</param>
public sealed record EnemyRoomChange(RoomId Room, string Key, int Count, IReadOnlyList<int> OldTypes, EnemyKind NewKind,
    IReadOnlyList<RoomId> Donors, RoomId? SoundFrom);

public sealed class EnemyRandomizer
{
    /// <summary>Room memory kept free above the expanded room, besides what the check measures.</summary>
    public const int MemoryMargin = 64 * 1024;

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
    private readonly Dictionary<string, RdxEnemyDonor> _donors = [];

    public EnemyRandomizer(RandoData data, RoomArchive archive, RandoSettings settings, Rng rng)
    {
        _data = data;
        _archive = archive;
        _settings = settings;
        _rng = rng;
    }

    /// <summary>Picks new enemies for every room that allows it and queues the room rebuilds.</summary>
    public List<EnemyRoomChange> Apply(ItemPatcher rooms)
    {
        var catalog = _data.Enemies;
        var scripted = catalog.ScriptedOpcodes.ToHashSet();
        var difficulty = Math.Clamp(_settings.EnemyDifficulty, 0, 3);
        var changes = new List<EnemyRoomChange>();

        foreach (var (key, room) in _data.Map.Rooms.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (catalog.KeepRooms.Contains(key))
                continue;
            var spec = (room.Enemies ?? []).FirstOrDefault(s => s.DoorRando == null || s.DoorRando == _settings.RandomDoors);
            if (spec?.Restricted == true)
                continue;
            var id = RoomId.ParseBioRand(key);
            if (!RoomTable.Contains(id))
                continue;

            var original = _archive.ReadRoom(id);
            var layout = RdxEnemyLayout.Read(original);
            if (!layout.IsConsistent || layout.Enemies.Any(e => !e.HasModel))
                continue;
            var targets = layout.Enemies.Where(e => catalog.Replaceable.Contains(e.Type)).ToList();
            if (targets.Count == 0)
                continue;
            var opcodes = CvScriptReader.ReadOpcodes(original).ToList();
            if (opcodes.Any(o => scripted.Contains(o.Name)))
                continue;
            // Enemies an event moves or animates keep their type (the event's motions are theirs).
            var controlled = opcodes.Where(o => catalog.WorkOpcodes.Contains(o.Name) && o.Bytes.Length >= 3 && o.Bytes.Span[1] == 1)
                .Select(o => (int)o.Bytes.Span[2]).ToHashSet();
            targets.RemoveAll(t => controlled.Contains(t.Index));
            if (targets.Count == 0)
                continue;

            var maxKind = Math.Min(spec?.MaxDifficulty ?? 3, 3);
            var candidates = catalog.Types
                .Where(k => k.Difficulty <= maxKind && k.MaxAt(difficulty) >= targets.Count)
                .Where(k => spec?.IncludeTypes is not { Length: > 0 } inc || inc.Contains(k.Type))
                .Where(k => spec?.ExcludeTypes is not { } exc || !exc.Contains(k.Type))
                .ToList();

            // Try kinds by weight until one fits the room.
            while (candidates.Count != 0)
            {
                var kind = _rng.NextWeighted(candidates.Select(k => (k, Weights[difficulty][k.Difficulty] * k.Weight)).ToList());
                candidates.Remove(kind);
                var plan = Plan(kind, targets);
                if (plan == null)
                    continue;
                RdxFile rebuilt;
                try
                {
                    var current = rooms.Current(id);
                    if (!RdxEnemyLayout.Read(current).IsConsistent)
                        throw new InvalidDataException($"{id}: enemy data not understood after earlier changes");
                    rebuilt = RdxEnemyRewriter.Replace(current, plan);
                }
                catch (RdxEnemyRewriter.ConflictException)
                {
                    continue;
                }
                if (rebuilt.Length > Expand.RoomPool - MemoryMargin)
                    continue;

                foreach (var nop in spec?.Nop ?? [])
                    rooms.Nop(id, nop);
                rooms.SetRebuild(id, file => RdxEnemyRewriter.Replace(file, plan));
                // The room's enemy sounds (sound pack bank 3) can follow when no other enemy needs them.
                var others = RdxEnemyLayout.Read(rebuilt).Enemies.Any(e => e.Type is > 0 and <= 30 && e.Type != kind.Type);
                var donorRooms = plan.Values.Select(d => d.Room).Distinct().ToList();
                changes.Add(new EnemyRoomChange(id, key, targets.Count, targets.Select(t => t.Type).ToList(), kind,
                    donorRooms, others ? null : donorRooms[0]));
                break;
            }
        }
        return changes;
    }

    /// <summary>Which donor each enemy gets: one per kind, or one or two zombie skins per room.</summary>
    private Dictionary<int, RdxEnemyDonor>? Plan(EnemyKind kind, List<RdxEnemyAssets> targets)
    {
        var donors = _rng.Shuffle(kind.Donors).Take(kind.Donors.Length > 1 && targets.Count > 2 ? 2 : 1)
            .Select(d => Donor(kind, d)).ToList();
        if (donors.Any(d => d == null))
            return null;
        return targets.Select((t, i) => (t.Index, donors[i % donors.Count]!)).ToDictionary(x => x.Index, x => x.Item2);
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
}
