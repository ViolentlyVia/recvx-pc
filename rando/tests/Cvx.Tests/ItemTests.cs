using Cvx.Formats;
using Cvx.Formats.Rdx;
using Cvx.Rando;
using Cvx.Rando.Data;
using Cvx.Rando.Items;

namespace Cvx.Tests;

public class ItemTests
{
    private static readonly RandoData Data = RandoData.Load();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheStatueKeepsItsRustedSword(bool doors)
    {
        // Found in play (seed 522131): taking RM_0150's statue pickup locks the door until a
        // Rusted Sword is used on the statue, so that pickup must stay the Rusted Sword.
        var settings = new RandoSettings { Seed = 1, ItemMode = ItemMode.Custom, RandomDoors = doors };
        var graph = doors
            ? Enumerable.Range(1, 100).Select(seed => Generator.MakeDoors(settings with { Seed = seed }, Data).Graph)
                .First(g => g.Locations.Any(l => l.GlobalId == 8))   // a run that uses the room
            : ItemGraph.Build(Data, settings);
        var statue = Assert.Single(graph.Locations, l => l.GlobalId == 8);
        Assert.Equal(103, statue.OriginalType);
        Assert.Equal(ItemPriority.Fixed, statue.Priority);
    }

    private static ItemPlacementResult Place(RandoSettings settings, out ItemGraph graph)
    {
        graph = ItemGraph.Build(Data, settings);
        return new ItemPlacer(graph, Data.Items, settings, Rng.For(settings.Seed, "items")).Place();
    }

    [Theory]
    [InlineData(ItemMode.Shuffle, true)]
    [InlineData(ItemMode.Custom, true)]
    [InlineData(ItemMode.Shuffle, false)]
    public void TwoHundredSeedsAreAllCompletable(ItemMode mode, bool chris)
    {
        var failures = new List<string>();
        for (var seed = 1; seed <= 200; seed++)
        {
            var settings = new RandoSettings { Seed = seed, ItemMode = mode, IncludeChris = chris };
            try
            {
                var placement = Place(settings, out var graph);
                var check = Playthrough.Check(graph, placement);
                if (!check.Completed)
                    failures.Add($"seed {seed}: {check.Message}");
                if (placement.ByGlobalId.Count != graph.Locations.Count)
                    failures.Add($"seed {seed}: {graph.Locations.Count - placement.ByGlobalId.Count} pickups left empty");
            }
            catch (ItemPlacementException ex)
            {
                failures.Add($"seed {seed}: {ex.Message}");
            }
        }
        Assert.Empty(failures);
    }

    [Fact]
    public void SameSeedSamePlacement()
    {
        var settings = new RandoSettings { Seed = 42 };
        var a = Place(settings, out _).ByGlobalId.OrderBy(x => x.Key).Select(x => (x.Key, x.Value.Type)).ToList();
        var b = Place(settings, out _).ByGlobalId.OrderBy(x => x.Key).Select(x => (x.Key, x.Value.Type)).ToList();
        var c = Place(settings with { Seed = 43 }, out _).ByGlobalId.OrderBy(x => x.Key).Select(x => (x.Key, x.Value.Type)).ToList();
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void ShuffleKeepsTheGamesItems(int seed)
    {
        // Shuffle mode moves the game's own items: the same non-key items, and no key placed
        // more often than the game has it. Spare key pickups become Green Herbs.
        var settings = new RandoSettings { Seed = seed };
        var placement = Place(settings, out var graph);
        var keys = ItemPlacer.RequiredKeys(graph.Rooms.Values).ToHashSet();
        var before = graph.Locations.GroupBy(l => l.OriginalType).ToDictionary(g => g.Key, g => g.Count());
        var after = placement.ByGlobalId.Values.GroupBy(a => a.Type).ToDictionary(g => g.Key, g => g.Count());
        var diff = before.Keys.Union(after.Keys).Order()
            .Where(t => t != 21)
            .Where(t => keys.Contains(t)
                ? after.GetValueOrDefault(t) > before.GetValueOrDefault(t)
                : after.GetValueOrDefault(t) != before.GetValueOrDefault(t))
            .Select(t => $"{Data.Items.NameOf(t)} {before.GetValueOrDefault(t)} -> {after.GetValueOrDefault(t)}")
            .ToList();
        Assert.Empty(diff);
    }

    [Fact]
    public void FixedPickupsKeepTheirItems()
    {
        var placement = Place(new RandoSettings { Seed = 3 }, out var graph);
        foreach (var l in graph.Locations.Where(l => l.Priority == ItemPriority.Fixed))
            Assert.Equal(l.OriginalType, placement.ByGlobalId[l.GlobalId].Type);
    }

    [Fact]
    public void RngIsStable()
    {
        // Pinned values: a change here changes every seed ever shared.
        var rng = Rng.For(12345, "items");
        Assert.Equal(new[] { 984, 624, 990 }, new[] { rng.Next(1000), rng.Next(1000), rng.Next(1000) });
    }

    [GameDataFact]
    public void GeneratedRoomsChangeOnlyItems()
    {
        // Rooms keep their size and change only item bytes (and script ranges some pickups
        // blank out, up to ~200 bytes), except where the item textures had to follow the new
        // ids; either way the loader reads exactly the texture entries the room has.
        using var rooms = RoomArchive.Open(TestData.Open());
        var result = Generator.Generate(new RandoSettings { Seed = 11 }, Data, rooms);
        Assert.NotEmpty(result.ChangedRooms);
        var rebuilt = 0;
        foreach (var (id, file) in result.ChangedRooms)
        {
            var original = rooms.ReadExpanded(id);
            if (original.Length == file.Length)
            {
                var diff = Enumerable.Range(0, original.Length).Count(i => original[i] != file.Data.Span[i]);
                Assert.InRange(diff, 1, 512);
            }
            else
            {
                rebuilt++;
            }
            if (RdxEnemyLayout.Read(new RdxFile(original)).IsConsistent)
                Assert.True(RdxEnemyLayout.Read(file).IsConsistent, $"{id}: the loader would read other texture entries");
            Assert.True(file.ToBuilder().ToFile().Data.Span.SequenceEqual(file.Data.Span));
        }
        Assert.InRange(rebuilt, 1, result.ChangedRooms.Count / 2);
    }
}
