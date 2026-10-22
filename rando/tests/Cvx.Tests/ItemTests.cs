using Cvx.Formats;
using Cvx.Formats.Script;
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

    [GameDataFact]
    public void TheCourtyardGateFollowsTheEmblem()
    {
        // the courtyard's gate version (RM_0031) loads once item flag 4 is set; the pickup holding
        // the Sp Alloy Emblem takes that flag, RM_0080's (the Padlock Key's spot) the emblem spot's
        using var rooms = RoomArchive.Open(TestData.Open());
        var swapped = 0;
        foreach (var seed in new[] { 1, 2, 3, 4, 5, 6 })
        {
            var result = Generator.Generate(new RandoSettings { Seed = seed }, Data, rooms);
            var spot = Data.Map.Rooms.SelectMany(r => (r.Value.Items ?? []).Select(i => (Room: r.Key, Item: i)))
                .First(x => x.Item.GlobalId is int g && result.Items!.ByGlobalId.TryGetValue(g, out var a) && a.Type == RoomTweaks.SpAlloyEmblem);
            var id = RoomId.ParseBioRand(spot.Room);
            int Flag(RoomId room, int area)
            {
                var file = result.ChangedRooms.TryGetValue(room, out var f) ? f : rooms.ReadRoom(room);
                var op = CvScriptReader.ReadOpcodes(file).First(o => o.Name == "ck_item" && o.Bytes.Span[4] == area);
                return (op.Bytes.Span[2] | op.Bytes.Span[3] << 8) & 0x3FF;
            }
            Assert.Equal(RoomTweaks.CourtyardFlag, Flag(id, spot.Item.Id));
            if (id != RoomId.ParseName("RM_0080"))
            {
                Assert.NotEqual(RoomTweaks.CourtyardFlag, Flag(RoomId.ParseName("RM_0080"), 2));
                swapped++;
            }
        }
        Assert.True(swapped > 0);
    }

    [GameDataTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoryChecksFollowTheirItems(bool doors)
    {
        // Rooms that check another room's pickup flag for a key item (RM_0090 the Briefcase's,
        // the stage 2 rooms the King Ant Object's, ...) check the flag of the pickup that holds
        // the item now: that pickup sets it when taken.
        using var rooms = RoomArchive.Open(TestData.Open());
        var followed = 0;
        foreach (var seed in new[] { 1, 2, 3, 4 })
        {
            var result = Generator.Generate(new RandoSettings { Seed = seed, RandomDoors = doors }, Data, rooms);
            var rule = StoryFlags.Rule(rooms, Data, result.Settings);
            // the placer puts those keys where a pickup flag is (all of them could be)
            foreach (var key in rule.Keys)
            {
                foreach (var (g, a) in result.Items!.ByGlobalId.Where(x => x.Value.Type == key && x.Value.IsKey))
                    Assert.Contains(g, rule.Locations);
            }
            foreach (var change in result.StoryFlags.Where(c => c.To != null))
            {
                var file = result.ChangedRooms[change.Room];
                // (opcodes put first in the start procedure, with random doors, move the rest along:
                // the same place in the same procedure, after what was added to procedure 0)
                var original = rooms.ReadRoom(change.Room);
                var p = CvScriptReader.ReadOpcodes(original).Single(o => o.Offset == change.Offset).Procedure;
                int Size0(Cvx.Formats.Rdx.RdxFile f) => f.Script.GetOffset(1) - f.Script.GetOffset(0);
                var at = file.ScriptOffset + file.Script.GetOffset(p) + (change.Offset - original.ScriptOffset - original.Script.GetOffset(p))
                    + (p == 0 ? Size0(file) - Size0(original) : 0);
                var op = CvScriptReader.ReadOpcodes(file).Single(o => o.Offset == at);
                Assert.Equal(change.To, (op.Bytes.Span[2] | op.Bytes.Span[3] << 8) & 0x3FF);
                if (!change.Why.StartsWith("follows ", StringComparison.Ordinal) || change.Why == "follows its spot")
                    continue;
                followed++;
                // the pickup holding the item sets that flag
                var name = change.Why["follows ".Length..];
                var holders = result.Items!.ByGlobalId.Where(x => Data.Items.NameOf(x.Value.Type) == name).Select(x => x.Key).ToHashSet();
                var sets = Data.Map.Rooms.SelectMany(r => (r.Value.Items ?? []).Where(i => i.GlobalId is int g && holders.Contains(g)).Select(i => (Room: RoomId.ParseBioRand(r.Key), i.Id)))
                    .Any(spot =>
                    {
                        var room = result.ChangedRooms.TryGetValue(spot.Room, out var f) ? f : rooms.ReadRoom(spot.Room);
                        return CvScriptReader.ReadOpcodes(room).Any(o => o.Name is "ck_item" or "set_ck_item" && o.Bytes.Span[4] == spot.Id &&
                            ((o.Bytes.Span[2] | o.Bytes.Span[3] << 8) & 0x3FF) == change.To);
                    }) || (holders.Contains(1005) && change.To == 18);   // RM_2070's scripted pickup (area 26)
                Assert.True(sets, $"seed {seed}: {change.Room} 0x{change.Offset:X} reads {change.To} for {name}, which no pickup of it sets");
            }
            // nothing is left on an old spot, but a key the logic did not need (the Turn Table Key)
            Assert.All(result.StoryFlags.Where(c => c.Why.EndsWith("(unchanged)", StringComparison.Ordinal)),
                c => Assert.DoesNotContain(result.Items!.ByGlobalId.Values, a => a.IsKey && c.Why.StartsWith(Data.Items.NameOf(a.Type) + " ", StringComparison.Ordinal)));
        }
        if (!doors)
            Assert.True(followed > 50, $"{followed} checks followed");
    }

    [GameDataFact]
    public void LaterRoomsKeepTheirPickups()
    {
        // Claire's later rooms took their pickup away when she had taken the same item in an
        // earlier room (RM_7020 after RM_3040, RM_7150 after RM_3070), and RM_4020 takes RM_8020's
        // away when it starts. They are separate pickups for the logic, so the scripts no longer do.
        using var rooms = RoomArchive.Open(TestData.Open());
        var result = Generator.Generate(new RandoSettings { Seed = 3 }, Data, rooms);
        var blanked = result.StoryFlags.Where(c => c.To == null).Select(c => (c.Room.Name, c.From)).ToHashSet();
        foreach (var expected in new[] { ("RM_7020", 300), ("RM_7020", 303), ("RM_7150", 339), ("RM_7150", 340), ("RM_7150", 341), ("RM_4020", 252), ("RM_4000", 247) })
            Assert.Contains(expected, blanked);
        foreach (var change in result.StoryFlags.Where(c => c.To == null))
        {
            var bytes = result.ChangedRooms[change.Room].Data.Span.Slice(change.Offset, 6);
            Assert.True(bytes.ToArray().All(b => b == 0xF4), $"{change.Room} 0x{change.Offset:X}");
        }
        // RM_7180's Bow Gun is RM_3130's own pickup (one location): its set stays
        Assert.DoesNotContain(result.StoryFlags, c => c.Room.Name is "RM_7180" or "RM_7181" && c.To == null);
    }
}
