using System.Globalization;
using System.Text;
using System.Text.Json;
using Cvx.Formats;
using Cvx.Formats.Rdx;
using Cvx.Rando.Data;
using Cvx.Rando.Items;

namespace Cvx.Rando;

public sealed record GenerationResult
{
    public required RandoSettings Settings { get; init; }
    public required IReadOnlyDictionary<RoomId, RdxFile> ChangedRooms { get; init; }
    public ItemPlacementResult? Items { get; init; }
    public IReadOnlyList<RoomPatch> Patches { get; init; } = [];
    /// <summary>Pickup amount per item type (rando.ini pickup_amounts); empty keeps the game's.</summary>
    public IReadOnlyDictionary<int, int> PickupAmounts { get; init; } = new Dictionary<int, int>();
    /// <summary>Claire's and Chris's starting items, when randomized.</summary>
    public IReadOnlyList<StartInventory> StartInventories { get; init; } = [];
    public required string Spoiler { get; init; }
}

/// <summary>Makes a randomized game from settings: decides everything, then patches the rooms.</summary>
public static class Generator
{
    public const int RandoIniVersion = 1;

    public static GenerationResult Generate(RandoSettings settings, RandoData data, RoomArchive archive)
    {
        ItemPlacementResult? items = null;
        var patcher = new ItemPatcher(data, archive);
        var amounts = new Dictionary<int, int>();

        var inventories = new List<StartInventory>();
        if (settings.RandomInventory)
        {
            var rng = Rng.For(settings.Seed, "inventory");
            inventories.Add(StartInventory.Make("claire", data.Items, settings, rng));
            if (settings.IncludeChris)
                inventories.Add(StartInventory.Make("chris", data.Items, settings, rng));
        }

        if (settings.RandomItems)
        {
            var graph = ItemGraph.Build(data, settings);
            var claireWeapons = inventories.FirstOrDefault(i => i.Character == "claire")?.Weapons(data.Items).ToList() ?? [];
            items = new ItemPlacer(graph, data.Items, settings, Rng.For(settings.Seed, "items"), claireWeapons).Place();
            patcher.Apply(items, settings);
            if (settings.ItemMode == ItemMode.Custom)
                amounts = PickupAmounts(data.Items, settings, Rng.For(settings.Seed, "amounts"));
        }

        var result = new GenerationResult
        {
            Settings = settings,
            ChangedRooms = patcher.ChangedRooms,
            Items = items,
            Patches = patcher.Patches,
            PickupAmounts = amounts,
            StartInventories = inventories,
            Spoiler = "",
        };
        return result with { Spoiler = Spoiler.Write(result, data) };
    }

    /// <summary>
    /// Pickup amounts per type, as BioRand's SetItemQuantityPickup: ammo between half and all of
    /// its usual maximum (scaled by the ammo setting), ink ribbons 1-2.
    /// </summary>
    private static Dictionary<int, int> PickupAmounts(ItemCatalog items, RandoSettings settings, Rng rng)
    {
        var result = new Dictionary<int, int>();
        foreach (var item in items.Items.OrderBy(i => i.Id))
        {
            if (item.Id == 31)
            {
                result[item.Id] = rng.Next(1, 3);
            }
            else if ((item.Is("ammo") || item.Id == 1) && item.MaxPickup > 0)
            {
                var max = (int)Math.Round(settings.AmmoQuantity / 8.0 * item.MaxPickup);
                var min = Math.Max(1, max / 2);
                result[item.Id] = rng.Next(min, Math.Max(min, max) + 1);
            }
        }
        return result;
    }

    /// <summary>
    /// Writes the mod folder recvx-pc loads (mod= in recvx.ini): the changed room archive,
    /// rando.ini and the spoiler log.
    /// </summary>
    public static void WriteMod(GenerationResult result, RoomArchive archive, string directory)
    {
        Directory.CreateDirectory(directory);
        var afs = Path.Combine(directory, GameData.RoomArchiveName);
        if (result.ChangedRooms.Count != 0)
            archive.WriteModified(afs, result.ChangedRooms);
        else if (File.Exists(afs))
            File.Delete(afs);

        File.WriteAllText(Path.Combine(directory, "rando.ini"), RandoIni(result), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "spoiler.txt"), result.Spoiler, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "settings.json"),
            JsonSerializer.Serialize(result.Settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static string RandoIni(GenerationResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Written by cvx-rando; read by recvx.exe (pc_rando.c).");
        sb.AppendLine($"version={RandoIniVersion}");
        sb.AppendLine($"seed={result.Settings.Seed}");
        if (result.Settings.RandomItems)
            sb.AppendLine("fix_rifle_stacking=1");
        if (result.Settings.NoNosferatuPoison)
            sb.AppendLine("no_nosferatu_poison=1");
        foreach (var inventory in result.StartInventories)
        {
            sb.AppendLine($"start_items_{inventory.Character}=" + string.Join(",",
                inventory.Items.Select(i => string.Create(CultureInfo.InvariantCulture, $"{i.Type}:{i.Count}"))));
        }
        if (result.PickupAmounts.Count != 0)
        {
            sb.AppendLine("pickup_amounts=" + string.Join(",",
                result.PickupAmounts.OrderBy(x => x.Key).Select(x => string.Create(CultureInfo.InvariantCulture, $"{x.Key}:{x.Value}"))));
        }
        return sb.ToString();
    }
}
