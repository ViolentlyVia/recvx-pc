using Cvx.Formats;
using Cvx.Rando;
using Cvx.Rando.Data;
using Cvx.Rando.Items;

namespace Cvx.Tests;

public class InventoryTests
{
    private static readonly RandoData Data = RandoData.Load();

    private static int SlotsUsed(StartInventory inv) => inv.Items.Sum(i => Data.Items.Get(i.Type)?.Slots ?? 1);

    [Fact]
    public void InventoriesFitAndStartWithAKnife()
    {
        for (var seed = 1; seed <= 300; seed++)
        {
            var settings = new RandoSettings { Seed = seed, RandomInventory = true };
            var rng = Rng.For(seed, "inventory");
            foreach (var who in new[] { "claire", "chris" })
            {
                var inv = StartInventory.Make(who, Data.Items, settings, rng);
                Assert.InRange(SlotsUsed(inv), 1, StartInventory.Slots);
                Assert.Contains(inv.Items, i => i.Type == 8);
                Assert.All(inv.Items, i => Assert.True(i.Count > 0));
                // two-slot items come first, as the game sorts them
                var twoSlot = inv.Items.Select(i => (Data.Items.Get(i.Type)?.Slots ?? 1) == 2).ToList();
                Assert.Equal(twoSlot.OrderByDescending(x => x), twoSlot);
            }
        }
    }

    [Theory]
    [InlineData(WeaponChoice.Sidearm, "sidearm")]
    [InlineData(WeaponChoice.Primary, "primary")]
    [InlineData(WeaponChoice.Powerful, "powerful")]
    public void WeaponKindIsRespected(WeaponChoice choice, string kind)
    {
        var settings = new RandoSettings { RandomInventory = true, StartWeapon1 = choice, StartWeapon2 = WeaponChoice.None };
        for (var seed = 1; seed <= 50; seed++)
        {
            var inv = StartInventory.Make("claire", Data.Items, settings with { Seed = seed }, Rng.For(seed, "inventory"));
            var weapons = inv.Weapons(Data.Items).ToList();
            Assert.Single(weapons);
            Assert.Equal(kind, Data.Items.Get(weapons[0])!.WeaponKind);
        }
    }

    [Fact]
    public void CustomModeDoesNotPlaceClairesStartingWeaponsAgain()
    {
        var settings = new RandoSettings { Seed = 5, ItemMode = ItemMode.Custom, RandomInventory = true };
        var inv = StartInventory.Make("claire", Data.Items, settings, Rng.For(settings.Seed, "inventory"));
        var weapons = inv.Weapons(Data.Items).ToList();
        Assert.NotEmpty(weapons);
        var graph = ItemGraph.Build(Data, settings);
        var placement = new ItemPlacer(graph, Data.Items, settings, Rng.For(settings.Seed, "items"), weapons).Place();
        Assert.DoesNotContain(placement.ByGlobalId.Values, a => weapons.Contains(a.Type) && a.Type != a.Location.OriginalType);
    }

    [Fact]
    public void RandoIniHasTheStartingItems()
    {
        var result = new GenerationResult
        {
            Settings = new RandoSettings { Seed = 1, RandomInventory = true, NoNosferatuPoison = true },
            ChangedRooms = new Dictionary<Cvx.Formats.RoomId, Cvx.Formats.Rdx.RdxFile>(),
            StartInventories = [new StartInventory("claire", [new(8, 1), new(9, 15)]), new StartInventory("chris", [new(8, 1)])],
            Spoiler = "",
        };
        var ini = Generator.RandoIni(result);
        Assert.Contains("start_items_claire=8:1,9:15", ini);
        Assert.Contains("start_items_chris=8:1", ini);
        Assert.Contains("no_nosferatu_poison=1", ini);
        Assert.Contains("version=1", ini);
    }

    [GameDataFact]
    public void TheSidePackIsSetWhereANewGameStarts()
    {
        using var rooms = RoomArchive.Open(TestData.Open());
        byte[] claire = [0x05, 0x01, 6, 0, 0, 0], chris = [0x05, 0x01, 7, 0, 0, 0];
        foreach (var (items, doors) in new[] { (false, false), (true, false), (true, true) })
        {
            var result = Generator.Generate(new RandoSettings { Seed = 4, RandomItems = items, RandomDoors = doors, StartSidePack = true }, Data, rooms);
            foreach (var room in new[] { "1000", "1010" })
            {
                var start = result.ChangedRooms[RoomId.ParseBioRand(room)].Script[0].ToArray();
                Assert.True(Contains(start, claire) && Contains(start, chris), $"{room} items {items} doors {doors}");
            }
            Assert.Contains("Side pack: from the start", result.Spoiler);
        }

        static bool Contains(byte[] haystack, byte[] needle) =>
            Enumerable.Range(0, haystack.Length - needle.Length + 1).Any(i => haystack.AsSpan(i, needle.Length).SequenceEqual(needle));
    }
}
