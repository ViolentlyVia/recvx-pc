using Cvx.Formats;
using Cvx.Formats.Rdx;
using Cvx.Formats.Script;
using Cvx.Rando;
using Cvx.Rando.Data;
using Cvx.Rando.Enemies;
using Cvx.Rando.Setup;

namespace Cvx.Tests;

public class EnemyTests
{
    private static readonly RandoData Data = RandoData.Load();

    [GameDataFact]
    public void EveryRoomWithEnemiesIsUnderstood()
    {
        // The enemy layout reader accounts for every texture entry the loader reads.
        using var rooms = RoomArchive.Open(TestData.Open());
        var bad = RoomTable.All
            .Select(id => (id, layout: RdxEnemyLayout.Read(rooms.ReadRoom(id))))
            .Where(x => x.layout.Enemies.Count != 0 && !x.layout.IsConsistent)
            .Select(x => x.id.Name)
            .ToList();
        Assert.Empty(bad);
    }

    [GameDataFact]
    public void DonorsAreUsable()
    {
        using var rooms = RoomArchive.Open(TestData.Open());
        foreach (var kind in Data.Enemies.Types)
        {
            Assert.NotEmpty(kind.Donors);
            foreach (var spec in kind.Donors)
            {
                var parts = spec.Split('#');
                var room = RoomId.ParseName(parts[0]);
                var donor = RdxEnemyDonor.From(room, rooms.ReadRoom(room), int.Parse(parts[1]));
                Assert.True(donor != null, $"{kind.Name}: {spec} cannot be copied");
                Assert.Equal(kind.Type, donor.Type);
                if (kind.Version is int v)
                    Assert.Equal(v, donor.ModelVersion);
            }
        }
    }

    [GameDataFact]
    public void SwappingKeepsRoomsLoadable()
    {
        // RM_0060's zombies as zombie dogs (checked in game): the loader reads exactly the
        // entries the room has, the room rebuilds byte for byte, and it fits the room memory.
        using var rooms = RoomArchive.Open(TestData.Open());
        var room = RoomId.ParseName("RM_0060");
        var donorRoom = RoomId.ParseName("RM_3011");
        var donor = RdxEnemyDonor.From(donorRoom, rooms.ReadRoom(donorRoom), 0, 0)!;
        var file = rooms.ReadRoom(room);
        var plan = RdxEnemyLayout.Read(file).Enemies.Where(e => e.Type == 1).ToDictionary(e => e.Index, _ => donor);
        var result = RdxEnemyRewriter.Replace(file, plan);

        var layout = RdxEnemyLayout.Read(result);
        Assert.True(layout.IsConsistent);
        Assert.All(plan.Keys, i => Assert.Equal(4, layout.Enemies[i].Type));
        Assert.Equal(1, layout.Enemies.Count(e => e.Type == 4 && e.Motion.Length != 0));
        Assert.True(result.ToBuilder().ToFile().Data.Span.SequenceEqual(result.Data.Span));
        Assert.True(Expand.FitsRoomPool(Prs.Compress(result.Data.Span), out _, out _));
    }

    [GameDataFact]
    public void GeneratedEnemyRoomsAreLoadable()
    {
        using var rooms = RoomArchive.Open(TestData.Open());
        foreach (var (seed, difficulty) in new[] { (5, 1), (6, 3) })
        {
            var result = Generator.Generate(new RandoSettings { Seed = seed, RandomEnemies = true, EnemyDifficulty = difficulty }, Data, rooms);
            Assert.InRange(result.Enemies.Count, 20, 120);
            foreach (var change in result.Enemies)
            {
                var file = result.ChangedRooms[change.Room];
                var layout = RdxEnemyLayout.Read(file);
                Assert.True(layout.IsConsistent, $"seed {seed} {change.Room}");
                Assert.True(layout.Enemies.Count(e => e.Type == change.NewKind.Type) >= change.Count);
                Assert.True(file.Length <= Expand.RoomPool, $"seed {seed} {change.Room}");

                // enemies that room events take control of keep their type (RM_0031's bats crashed)
                var original = rooms.ReadRoom(change.Room);
                var controlled = CvScriptReader.ReadOpcodes(original)
                    .Where(o => Data.Enemies.WorkOpcodes.Contains(o.Name) && o.Bytes.Span[1] == 1)
                    .Select(o => (int)o.Bytes.Span[2]).ToHashSet();
                var before = RdxEnemyLayout.Read(original).Enemies;
                Assert.All(controlled.Where(i => i < before.Count), i => Assert.Equal(before[i].Entry.Type, layout.Enemies[i].Entry.Type));
            }
            // every placed kind is one the setting allows in that many
            Assert.All(result.Enemies, c => Assert.True(c.Count <= c.NewKind.MaxAt(difficulty)));
        }
    }

    [GameDataFact]
    public void SameSeedSameEnemies()
    {
        using var rooms = RoomArchive.Open(TestData.Open());
        string Text(int seed, bool placements = false)
        {
            var result = Generator.Generate(new RandoSettings { Seed = seed, RandomItems = false, RandomEnemies = true, RandomEnemyPlacements = placements }, Data, rooms);
            return string.Join("\n", result.Enemies.Select(c => $"{c.Room} {c.Count} {c.NewKind.Name} {string.Join(",", c.Donors)}")
                .Concat(result.ChangedRooms.Where(_ => placements).OrderBy(r => r.Key)
                    .Select(r => $"{r.Key} {string.Join(" ", r.Value.Enemies.ToArray().Select(e => $"{e.Position.X},{e.Position.Z},{e.Rotation.Y}"))}")));
        }
        Assert.Equal(Text(8), Text(8));
        Assert.NotEqual(Text(8), Text(9));
        // random placements too (the rooms' spots are found in parallel)
        Assert.Equal(Text(8, true), Text(8, true));
    }

    [GameDataFact]
    public void TheSlidersPickTheKinds()
    {
        // BioRand's per-enemy sliders: a kind at 0 never comes, the rest by their ratios
        using var rooms = RoomArchive.Open(TestData.Open());
        var only = Data.Enemies.Types.ToDictionary(k => k.Name, k => k.Name is "Zombie Dog" or "Hunter" ? 7 : 0);
        var result = Generator.Generate(new RandoSettings { Seed = 11, RandomItems = false, RandomEnemies = true, EnemyDifficulty = 3, EnemyRatios = only }, Data, rooms);
        Assert.NotEmpty(result.Enemies);
        Assert.All(result.Enemies, c => Assert.Contains(c.NewKind.Name, new[] { "Zombie Dog", "Hunter" }));
        Assert.Contains(result.Enemies, c => c.NewKind.Name == "Zombie Dog");

        // settings saved when the zombie dog was called Cerberus keep its ratio
        var dog = Data.Enemies.Types.Single(k => k.Name == "Zombie Dog");
        Assert.Equal(0, dog.RatioIn(new RandoSettings { EnemyRatios = new() { ["Cerberus"] = 0 } }));
        Assert.Contains(result.Enemies, c => c.NewKind.Name == "Hunter");

        // without ratios, untested kinds never come and the old mix is unchanged
        Assert.All(Data.Enemies.Types.Where(k => k.Untested), k => Assert.Equal(0, k.RatioIn(new RandoSettings())));
        Assert.All(Data.Enemies.Types.Where(k => !k.Untested), k => Assert.Equal(EnemyKind.DefaultRatio, k.RatioIn(new RandoSettings())));
        var plain = Generator.Generate(new RandoSettings { Seed = 11, RandomItems = false, RandomEnemies = true }, Data, rooms);
        Assert.DoesNotContain(plain.Enemies, c => c.NewKind.Untested);
    }

    [GameDataFact]
    public void UntestedKindsFitTheirRooms()
    {
        // each kind not yet seen in the game, alone at the top of its slider: rooms rebuild,
        // read back and fit the room memory (the game itself is the owner's test)
        using var rooms = RoomArchive.Open(TestData.Open());
        foreach (var kind in Data.Enemies.Types.Where(k => k.Untested))
        {
            var only = Data.Enemies.Types.ToDictionary(k => k.Name, k => k == kind ? 7 : 0);
            var result = Generator.Generate(new RandoSettings { Seed = 12, RandomItems = false, RandomEnemies = true, EnemyDifficulty = 3, EnemyRatios = only }, Data, rooms);
            Assert.True(result.Enemies.Count > 0, $"{kind.Name}: no room took it");
            foreach (var change in result.Enemies)
            {
                Assert.Equal(kind.Name, change.NewKind.Name);
                var file = result.ChangedRooms[change.Room];
                Assert.True(RdxEnemyLayout.Read(file).IsConsistent, $"{kind.Name} {change.Room}");
                Assert.True(Expand.FitsRoomPool(Prs.Compress(file.Data.Span), out _, out _), $"{kind.Name} {change.Room}");
            }
        }
    }

    [GameDataFact]
    public void RandomPlacementsStandOnTheFloor()
    {
        // BioRand's random placements: placed enemies start on the floor worked out for their
        // room, with room for their size; the room's own enemies keep their numbers (the scripts
        // name them so); new ones have death flags of their own, checked at the room's start
        // (set_ck_enemy) and set when they die (ck_death); ones that go are marked killed first
        using var rooms = RoomArchive.Open(TestData.Open());
        var settings = new RandoSettings { Seed = 21, RandomItems = false, RandomEnemies = true, RandomEnemyPlacements = true, EnemyQuantity = 4, EnemyRooms = 6 };
        var result = Generator.Generate(settings, Data, rooms);
        var placed = result.Enemies.Where(c => c.Moved).ToList();
        Assert.InRange(placed.Count, 30, 120);
        Assert.Contains(placed, c => c.Added != 0);
        Assert.Contains(placed, c => c.Hidden != 0);
        Assert.NotEmpty(result.EnemiesCleared);
        Assert.Equal(0, result.SharedEnemyFlags);

        var used = EnemyFlags.Of(rooms).Used;
        var owners = new Dictionary<int, (int, int)>();
        foreach (var c in placed)
        {
            var original = rooms.ReadRoom(c.Room);
            var before = RdxEnemyLayout.Read(original).Enemies;
            var file = result.ChangedRooms[c.Room];
            var after = RdxEnemyLayout.Read(file);
            Assert.True(after.IsConsistent, $"{c.Room}");
            Assert.True(file.Length <= Expand.RoomPool - EnemyRandomizer.MemoryMargin, $"{c.Room}");
            Assert.Equal(before.Count + c.Added, after.Enemies.Count);
            Assert.All(after.Enemies, e => Assert.Equal(e.Index, e.Entry.Index & 0xFF));

            var spots = EnemySpots.Find(original, EnemySpots.Known(c.Room, rooms, Data));
            var moved = after.Enemies.Where(e => e.Index >= before.Count || !e.Entry.Position.Equals(before[e.Index].Entry.Position)).ToList();
            Assert.Equal(c.Count, moved.Count);
            Assert.All(moved, e =>
            {
                Assert.Equal(c.NewKind.Type, e.Type);
                var spot = spots.Single(s => s.Position.Equals(e.Entry.Position));
                Assert.True(spot.Fits(c.NewKind.Radius), $"{c.Room}: {c.NewKind.Name} at {spot}");
            });

            var ops = CvScriptReader.ReadOpcodes(file);
            for (var i = before.Count; i < after.Enemies.Count; i++)
            {
                var start = ops.Single(o => o.Procedure == 0 && o.Opcode == EnemyFlags.SetCkEnemy && o.Bytes.Span[1] == i);
                var frame = ops.Single(o => o.Procedure == 1 && o.Opcode == EnemyFlags.CkDeath && o.Bytes.Span[1] == i);
                var flag = start.Bytes.Span[2] | start.Bytes.Span[3] << 8;
                Assert.Equal(flag, frame.Bytes.Span[2] | frame.Bytes.Span[3] << 8);
                Assert.DoesNotContain(flag, used);
                Assert.InRange(flag, 0, EnemyFlags.Count - 1);
                // one room number per flag (its versions share them)
                Assert.Equal((c.Room.Stage, c.Room.Room), owners.TryAdd(flag, (c.Room.Stage, c.Room.Room)) ? (c.Room.Stage, c.Room.Room) : owners[flag]);
            }
            var marked = ops.TakeWhile(o => o.Procedure == 0 && o.Name == "set").Count(o => o.Bytes.Span[1] == 3);
            Assert.Equal(c.Hidden, marked);
        }
        // the biggest rooms still expand in place in the room memory (compressing takes a while)
        foreach (var c in placed.OrderByDescending(c => result.ChangedRooms[c.Room].Length).Take(3))
            Assert.True(Expand.FitsRoomPool(Prs.Compress(result.ChangedRooms[c.Room].Data.Span), out _, out _), $"{c.Room}");

        // off, the settings leave share codes as they were
        Assert.DoesNotContain("Placement", Encode(new RandoSettings { Seed = 3, RandomEnemies = true }));
        Assert.Contains("Placement", Encode(new RandoSettings { Seed = 3, RandomEnemies = true, RandomEnemyPlacements = true }));
    }

    /// <summary>The settings JSON inside a share code.</summary>
    private static string Encode(RandoSettings settings)
    {
        var code = ShareCode.Encode(settings);
        var b64 = code[ShareCode.Prefix.Length..].Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
        using var input = new System.IO.Compression.DeflateStream(new MemoryStream(Convert.FromBase64String(b64)), System.IO.Compression.CompressionMode.Decompress);
        using var reader = new StreamReader(input);
        return reader.ReadToEnd();
    }

    [GameDataFact]
    public void SpotsMatchBioRandsRecordedPlaces()
    {
        // BioRand recorded where enemies can stand in a few Code: Veronica rooms by playing. They
        // seed the floors as the generator uses them (a floor only reached by stairs has no
        // other start); the walls, the room enemies need and the rule that a floor must join an
        // entrance's by stairs or a slope keep nearly all of them (not those near an entrance,
        // where placements never start an enemy)
        using var rooms = RoomArchive.Open(TestData.Open());
        var missed = new List<string>();
        var total = 0;
        foreach (var group in Data.EnemyPositions.GroupBy(p => p.RoomId))
        {
            var file = rooms.ReadRoom(group.Key);
            var spots = EnemySpots.Find(file, EnemySpots.Known(group.Key, rooms, Data));
            var entrances = file.Players.ToArray().Select(p => p.Position).ToList();
            foreach (var p in group)
            {
                if (entrances.Any(e => Distance(e, p.X, p.Z) < EnemySpots.EntranceDistances[0] + 4))
                    continue;
                total++;
                if (!spots.Any(s => Distance(s.Position, p.X, p.Z) <= 4))
                    missed.Add($"{group.Key} ({p.X}, {p.Y}, {p.Z})");
            }
        }
        Assert.True(total >= 15, $"{total} recorded places");
        Assert.True(missed.Count <= total / 10, string.Join("; ", missed));
    }

    private static float Distance(RdxVectorF a, float x, float z) => MathF.Sqrt((a.X - x) * (a.X - x) + (a.Z - z) * (a.Z - z));

    [GameDataFact]
    public void BatsAtNoneLeave()
    {
        // The owner's seed 428946: Bats at None, yet RM_0130's own bats stayed. Now only bats an
        // event moves stay (RM_1030 one, RM_4010 five), and what takes a bat's place starts on
        // the floor (bats hang at 26-44).
        using var rooms = RoomArchive.Open(TestData.Open());
        var ratios = Data.Enemies.Types.ToDictionary(k => k.Name, k => k.Name == "Bat" ? 0 : k.Untested ? 0 : 4);
        foreach (var difficulty in new[] { 0, 3 })
        {
            var result = Generator.Generate(new RandoSettings { Seed = 5, RandomItems = false, RandomEnemies = true, EnemyDifficulty = difficulty, EnemyRatios = ratios }, Data, rooms);
            var left = new List<string>();
            foreach (var id in RoomTable.All.Where(id => Data.Map.Rooms.ContainsKey(id.BioRandId)))
            {
                var file = result.ChangedRooms.TryGetValue(id, out var f) ? f : rooms.ReadRoom(id);
                var original = rooms.ReadRoom(id);
                var controlled = CvScriptReader.ReadOpcodes(original)
                    .Where(o => Data.Enemies.WorkOpcodes.Contains(o.Name) && o.Bytes.Span[1] == 1).Select(o => (int)o.Bytes.Span[2]).ToHashSet();
                var enemies = file.Enemies.ToArray();
                for (var i = 0; i < enemies.Length; i++)
                {
                    if (enemies[i].Type == 7 && !controlled.Contains(i))
                        left.Add($"{id.Name}[{i}]");
                }
                var before = original.Enemies.ToArray();
                if (!result.ChangedRooms.ContainsKey(id))
                    continue;
                var spots = EnemySpots.Find(original, EnemySpots.Known(id, rooms, Data)).Select(s => s.Position).ToList();
                for (var i = 0; i < before.Length && i < enemies.Length; i++)
                {
                    if (before[i].Type == 7 && enemies[i].Type != 7)
                        Assert.Contains(enemies[i].Position, spots);
                }
            }
            Assert.True(left.Count == 0, $"difficulty {difficulty}: bats left in {string.Join(", ", left)}");
        }
    }
}

