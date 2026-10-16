using System.Globalization;
using System.Text;
using System.Text.Json;
using Cvx.Formats;
using Cvx.Formats.Rdx;
using Cvx.Rando.Data;
using Cvx.Rando.Doors;
using Cvx.Rando.Enemies;
using Cvx.Rando.Items;
using Cvx.Rando.Music;

namespace Cvx.Rando;

public sealed record GenerationResult
{
    public required RandoSettings Settings { get; init; }
    public required IReadOnlyDictionary<RoomId, RdxFile> ChangedRooms { get; init; }
    public ItemPlacementResult? Items { get; init; }
    public DoorPlan? Doors { get; init; }
    /// <summary>Rooms whose enemies changed.</summary>
    public IReadOnlyList<EnemyRoomChange> Enemies { get; init; } = [];
    /// <summary>Music tracks swapped (rando.ini bgm_map).</summary>
    public IReadOnlyList<MusicSwap> Music { get; init; } = [];
    /// <summary>Door layouts made until one could be finished.</summary>
    public int DoorAttempts { get; init; }
    /// <summary>rando_doors.txt, with random doors.</summary>
    public string? DoorTable { get; init; }
    /// <summary>The room graph the logic used (Graphviz and Visual Studio formats).</summary>
    public string? GraphDot { get; init; }
    public string? GraphDgml { get; init; }
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
    public const int MaxDoorAttempts = 200;

    /// <summary>
    /// Rooms to save in (BioRand ids): rooms whose script has a typewriter, and the rooms the
    /// data calls box rooms.
    /// </summary>
    public static HashSet<string> SaveRoomKeys(RandoData data)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, room) in data.Map.Rooms)
        {
            var box = (room.DoorRando ?? []).Any(d => string.Equals(d.Category, "box", StringComparison.OrdinalIgnoreCase));
            if (box || SaveRooms.Typewriters.Contains(key))
                result.Add(key);
        }
        return result;
    }

    /// <param name="progress">Told what step is running (for a status line).</param>
    public static GenerationResult Generate(RandoSettings settings, RandoData data, RoomArchive archive, IProgress<string>? progress = null)
    {
        // Random doors need random items: with the game's own, keys end up behind the doors
        // they open.
        if (settings.RandomDoors)
            settings = settings with { RandomItems = true };
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

        var claireWeapons = inventories.FirstOrDefault(i => i.Character == "claire")?.Weapons(data.Items).ToList() ?? [];

        DoorPlan? doors = null;
        ItemGraph? graph = null;
        HashSet<string>? saveRooms = null;
        var doorAttempts = 0;
        if (settings.RandomDoors)
        {
            saveRooms = SaveRoomKeys(data);
            progress?.Report("Making the door layout...");
            var layout = MakeDoors(settings, data, claireWeapons);
            (doors, graph, doorAttempts) = (layout.Plan, layout.Graph, layout.Attempts);
            if (settings.RandomItems)
                items = layout.Items;
        }
        else if (settings.RandomItems)
        {
            progress?.Report("Placing items...");
            graph = ItemGraph.Build(data, settings);
            items = PlaceItems(data, settings, graph, claireWeapons);
        }
        if (items != null)
        {
            progress?.Report("Patching rooms...");
            patcher.Apply(items, settings);
            RoomTweaks.Apply(patcher, settings, data);
        }
        if (settings.StartSidePack)
            RoomTweaks.ApplySidePack(patcher);

        if (settings.RandomEnemies)
            progress?.Report("Choosing enemies...");
        var enemies = settings.RandomEnemies
            ? new EnemyRandomizer(data, archive, settings, Rng.For(settings.Seed, "enemies")).Apply(patcher)
            : [];

        var music = settings.RandomMusic && archive.Data is { } gameData
            ? MusicRandomizer.Shuffle(data.Music, MusicRandomizer.Present(gameData), Rng.For(settings.Seed, "music"))
            : [];

        if (settings.RandomItems && settings.ItemMode == ItemMode.Custom)
            amounts = PickupAmounts(data.Items, settings, Rng.For(settings.Seed, "amounts"));

        var result = new GenerationResult
        {
            Settings = settings,
            ChangedRooms = patcher.ChangedRooms,
            Items = items,
            Doors = doors,
            DoorAttempts = doorAttempts,
            Enemies = enemies,
            Music = music,
            DoorTable = doors == null ? null : Doors.DoorTable.Write(doors, data),
            GraphDot = graph == null ? null : GraphExport.Dot(graph, data.Items, saveRooms),
            GraphDgml = graph == null ? null : GraphExport.Dgml(graph, data.Items, saveRooms),
            Patches = patcher.Patches,
            PickupAmounts = amounts,
            StartInventories = inventories,
            Spoiler = "",
        };
        return result with { Spoiler = Spoiler.Write(result, data) };
    }

    private static ItemPlacementResult PlaceItems(RandoData data, RandoSettings settings, ItemGraph graph, IReadOnlyCollection<int> startWeapons) =>
        new ItemPlacer(graph, data.Items, settings, Rng.For(settings.Seed, "items"), startWeapons).Place();

    public sealed record DoorLayout(DoorPlan Plan, ItemGraph Graph, ItemPlacementResult Items, int Attempts);

    /// <summary>
    /// Door layouts until one the item logic can finish, checked again by <see cref="Playthrough"/>.
    /// </summary>
    public static DoorLayout MakeDoors(RandoSettings settings, RandoData data, IReadOnlyCollection<int>? startWeapons = null)
    {
        settings = settings with { RandomItems = true };
        var randomizer = new DoorRandomizer(data, SaveRoomKeys(data), settings.IncludeChris);
        for (var attempt = 0; attempt < MaxDoorAttempts; attempt++)
        {
            var plan = randomizer.Make(Rng.For(settings.Seed, $"doors{attempt}"));
            if (plan == null)
                continue;
            var graph = ItemGraph.Build(data, settings, plan);
            ItemPlacementResult placement;
            try
            {
                placement = PlaceItems(data, settings, graph, startWeapons ?? []);
            }
            catch (ItemPlacementException)
            {
                continue;
            }
            if (Playthrough.Check(graph, placement).Completed)
                return new DoorLayout(plan, graph, placement, attempt + 1);
        }
        throw new ItemPlacementException($"No door layout for seed {settings.Seed} could be finished (try another seed).");
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
    /// rando.ini, rando_doors.txt, the spoiler log and the room graph.
    /// </summary>
    public static void WriteMod(GenerationResult result, RoomArchive archive, string directory, IProgress<string>? progress = null)
    {
        Directory.CreateDirectory(directory);
        var afs = Path.Combine(directory, GameData.RoomArchiveName);
        progress?.Report($"Writing {result.ChangedRooms.Count} rooms...");
        if (result.ChangedRooms.Count != 0)
            archive.WriteModified(afs, result.ChangedRooms);
        else if (File.Exists(afs))
            File.Delete(afs);

        void Write(string name, string? text)
        {
            var path = Path.Combine(directory, name);
            if (text != null)
                File.WriteAllText(path, text, new UTF8Encoding(false));
            else if (File.Exists(path))
                File.Delete(path);
        }
        Write("rando.ini", RandoIni(result));
        if (archive.Data is { } data)
        {
            if (result.Enemies.Count != 0)
                progress?.Report("Writing enemy sounds...");
            EnemySounds.Write(data, result.Enemies, directory);
        }
        Write(DoorTable.FileName, result.DoorTable);
        Write("spoiler.txt", result.Spoiler);
        Write("graph.dot", result.GraphDot);
        Write("graph.dgml", result.GraphDgml);
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
        if (result.Doors is { } doors)
        {
            var start = RoomId.ParseBioRand(doors.StartRoom);
            sb.AppendLine($"start={start.Stage},{start.Room},{doors.StartEntrance},{start.Variant}");
        }
        foreach (var inventory in result.StartInventories)
        {
            sb.AppendLine($"start_items_{inventory.Character}=" + string.Join(",",
                inventory.Items.Select(i => string.Create(CultureInfo.InvariantCulture, $"{i.Type}:{i.Count}"))));
        }
        var swapped = result.Music.Where(m => m.From != m.To).ToList();
        if (swapped.Count != 0)
            sb.AppendLine("bgm_map=" + string.Join(",", swapped.Select(m => string.Create(CultureInfo.InvariantCulture, $"{m.From}:{m.To}"))));
        if (result.PickupAmounts.Count != 0)
        {
            sb.AppendLine("pickup_amounts=" + string.Join(",",
                result.PickupAmounts.OrderBy(x => x.Key).Select(x => string.Create(CultureInfo.InvariantCulture, $"{x.Key}:{x.Value}"))));
        }
        return sb.ToString();
    }
}
