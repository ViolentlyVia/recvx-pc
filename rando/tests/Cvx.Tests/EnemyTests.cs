using Cvx.Formats;
using Cvx.Formats.Rdx;
using Cvx.Formats.Script;
using Cvx.Rando;
using Cvx.Rando.Data;

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
        // RM_0060's zombies as Cerberus dogs (checked in game): the loader reads exactly the
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
        string Text(int seed) => string.Join("\n", Generator.Generate(
                new RandoSettings { Seed = seed, RandomItems = false, RandomEnemies = true }, Data, rooms).Enemies
            .Select(c => $"{c.Room} {c.NewKind.Name} {string.Join(",", c.Donors)}"));
        Assert.Equal(Text(8), Text(8));
        Assert.NotEqual(Text(8), Text(9));
    }
}
