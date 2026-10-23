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
using Cvx.Rando.Npcs;

namespace Cvx.Rando;

public sealed record GenerationResult
{
    public required RandoSettings Settings { get; init; }
    public required IReadOnlyDictionary<RoomId, RdxFile> ChangedRooms { get; init; }
    public ItemPlacementResult? Items { get; init; }
    public DoorPlan? Doors { get; init; }
    /// <summary>Rooms whose enemies changed.</summary>
    public IReadOnlyList<EnemyRoomChange> Enemies { get; init; } = [];
    /// <summary>Random enemy placements: rooms left without enemies.</summary>
    public IReadOnlyList<RoomId> EnemiesCleared { get; init; } = [];
    /// <summary>Random enemy placements: new enemies that share a death flag with another room's.</summary>
    public int SharedEnemyFlags { get; init; }
    /// <summary>Music tracks swapped (rando.ini bgm_map), some of them for other games' tracks.</summary>
    public IReadOnlyList<MusicSwap> Music { get; init; } = [];
    /// <summary>Games the settings ask music from that the BioRand folder does not have.</summary>
    public IReadOnlyList<string> MissingMusicGames { get; init; } = [];
    /// <summary>The loudness other games' music is matched to (data/music.json targetRms).</summary>
    public double MusicTargetRms { get; init; }
    /// <summary>The NPCs' new looks and voices, when they are shuffled.</summary>
    public NpcResult? Npcs { get; init; }
    /// <summary>BioRand's title call that replaces the game's (ADV.AFS), when it is randomized.</summary>
    public LibraryTrack? TitleCall { get; init; }
    /// <summary>Door layouts made until one could be finished.</summary>
    public int DoorAttempts { get; init; }
    /// <summary>rando_doors.txt, with random doors.</summary>
    public string? DoorTable { get; init; }
    /// <summary>The room graph the logic used (Graphviz and Visual Studio formats).</summary>
    public string? GraphDot { get; init; }
    public string? GraphDgml { get; init; }
    public IReadOnlyList<RoomPatch> Patches { get; init; } = [];
    /// <summary>Item flags rooms read as story progress, pointed at the items' new spots (see <see cref="Items.StoryFlags"/>).</summary>
    public IReadOnlyList<StoryFlagChange> StoryFlags { get; init; } = [];
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
    /// <param name="musicLibrary">BioRand's music, for <see cref="RandoSettings.MusicGames"/> other than this game.</param>
    /// <param name="characters">BioRand's characters, for <see cref="RandoSettings.NpcCharacters"/>.</param>
    public static GenerationResult Generate(RandoSettings settings, RandoData data, RoomArchive archive, IProgress<string>? progress = null,
        MusicLibrary? musicLibrary = null, CharacterLibrary? characters = null)
    {
        // Random doors need random items: with the game's own, keys end up behind the doors
        // they open.
        if (settings.RandomDoors)
            settings = settings with { RandomItems = true };
        ItemPlacementResult? items = null;
        List<StoryFlagChange> storyFlags = [];
        var patcher = new ItemPatcher(data, archive);
        var amounts = new Dictionary<int, int>();

        // With random doors one character plays the whole run.
        var inventories = new List<StartInventory>();
        var first = settings.RandomDoors && settings.DoorCharacter == Character.Chris ? "chris" : "claire";
        if (settings.RandomInventory)
        {
            var rng = Rng.For(settings.Seed, "inventory");
            if (settings.RandomDoors)
            {
                inventories.Add(StartInventory.Make(first, data.Items, settings, rng));
            }
            else
            {
                inventories.Add(StartInventory.Make("claire", data.Items, settings, rng));
                if (settings.IncludeChris)
                    inventories.Add(StartInventory.Make("chris", data.Items, settings, rng));
            }
        }

        // The weapons the first player starts with (without a random inventory, Claire's and
        // Chris's own knives).
        var claireWeapons = inventories.FirstOrDefault(i => i.Character == first)?.Weapons(data.Items).ToList() ?? [8];

        DoorPlan? doors = null;
        ItemGraph? graph = null;
        HashSet<string>? saveRooms = null;
        var doorAttempts = 0;
        if (settings.RandomDoors)
        {
            saveRooms = SaveRoomKeys(data);
            progress?.Report("Making the door layout...");
            var layout = MakeDoors(settings, data, claireWeapons, StoryFlags.Rule(archive, data, settings));
            (doors, graph, doorAttempts) = (layout.Plan, layout.Graph, layout.Attempts);
            if (settings.RandomItems)
                items = layout.Items;
        }
        else if (settings.RandomItems)
        {
            progress?.Report("Placing items...");
            graph = ItemGraph.Build(data, settings);
            items = PlaceItems(data, settings, graph, claireWeapons, StoryFlags.Rule(archive, data, settings));
        }
        if (items != null)
        {
            progress?.Report("Patching rooms...");
            patcher.Apply(items, settings);
            RoomTweaks.Apply(patcher, settings, data);
            var swap = RoomTweaks.TieCourtyardToEmblem(patcher, archive, data, items, settings);
            storyFlags = StoryFlags.Apply(patcher, archive, data, items, settings, swap);
        }
        if (settings.StartSidePack)
            RoomTweaks.ApplySidePack(patcher);

        if (settings.RandomEnemies)
            progress?.Report("Choosing enemies...");
        var enemyRandomizer = settings.RandomEnemies
            ? new EnemyRandomizer(data, archive, settings, Rng.For(settings.Seed, "enemies"), Rng.For(settings.Seed, "placements"))
            : null;
        var enemies = enemyRandomizer?.Apply(patcher) ?? [];

        if (settings.RandomNpcs)
            progress?.Report("Shuffling the NPCs...");
        var npcs = settings.RandomNpcs
            ? new NpcRandomizer(data, archive, Rng.For(settings.Seed, "npcs"), characters, settings.NpcCharacters).Apply(patcher)
            : null;

        List<MusicSwap> music = [];
        List<string> missingMusic = [];
        if (settings.RandomMusic && archive.Data is { } gameData)
        {
            var games = settings.MusicGames.Select(g => g.ToLowerInvariant()).ToHashSet();
            var own = games.Contains(MusicLibrary.OwnGame);
            var imports = musicLibrary?.Tracks.Where(t => games.Contains(t.Game)).ToList() ?? [];
            missingMusic = games.Where(g => g != MusicLibrary.OwnGame && musicLibrary?.Games.Contains(g) != true).Order().ToList();
            // no music of any chosen game here: the game's own, shuffled
            if (imports.Count == 0)
                own = true;
            music = MusicRandomizer.Mix(data.Music, MusicRandomizer.Present(gameData), imports, own, Rng.For(settings.Seed, "music"));
        }

        var titleCall = settings.RandomTitleCall && musicLibrary is { TitleCalls.Count: > 0 } titles
            ? Rng.For(settings.Seed, "title").NextOf(titles.TitleCalls)
            : null;

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
            EnemiesCleared = enemyRandomizer?.Cleared ?? [],
            SharedEnemyFlags = enemyRandomizer?.SharedFlags ?? 0,
            Music = music,
            MissingMusicGames = missingMusic,
            MusicTargetRms = data.Music.TargetRms,
            TitleCall = titleCall,
            Npcs = npcs,
            DoorTable = doors == null ? null : Doors.DoorTable.Write(doors, data, archive),
            GraphDot = graph == null ? null : GraphExport.Dot(graph, data.Items, saveRooms),
            GraphDgml = graph == null ? null : GraphExport.Dgml(graph, data.Items, saveRooms),
            Patches = patcher.Patches,
            StoryFlags = storyFlags,
            PickupAmounts = amounts,
            StartInventories = inventories,
            Spoiler = "",
        };
        return result with { Spoiler = Spoiler.Write(result, data) };
    }

    private static ItemPlacementResult PlaceItems(RandoData data, RandoSettings settings, ItemGraph graph, IReadOnlyCollection<int> startWeapons,
        StoryFlags.FlagRule? flagRule) =>
        new ItemPlacer(graph, data.Items, settings, Rng.For(settings.Seed, "items"), startWeapons, flagRule).Place();

    public sealed record DoorLayout(DoorPlan Plan, ItemGraph Graph, ItemPlacementResult Items, int Attempts);

    /// <summary>
    /// Door layouts until one the item logic can finish, checked again by <see cref="Playthrough"/>.
    /// </summary>
    /// <param name="flagRule">See <see cref="StoryFlags.Rule"/>.</param>
    public static DoorLayout MakeDoors(RandoSettings settings, RandoData data, IReadOnlyCollection<int>? startWeapons = null,
        StoryFlags.FlagRule? flagRule = null)
    {
        settings = settings with { RandomItems = true };
        var randomizer = new DoorRandomizer(data, SaveRoomKeys(data), settings);
        string? why = null;
        for (var attempt = 0; attempt < MaxDoorAttempts; attempt++)
        {
            var plan = randomizer.Make(Rng.For(settings.Seed, $"doors{attempt}"));
            if (plan == null)
            {
                why = randomizer.LastFailure;
                continue;
            }
            var graph = ItemGraph.Build(data, settings, plan);
            ItemPlacementResult placement;
            try
            {
                placement = PlaceItems(data, settings, graph, startWeapons ?? [], flagRule);
            }
            catch (ItemPlacementException e)
            {
                why = e.Message;
                continue;
            }
            var check = Playthrough.Check(graph, placement);
            why = check.Completed ? Playthrough.FindTrap(graph, placement) : check.Message;
            if (why == null)
                return new DoorLayout(plan, graph, placement, attempt + 1);
        }
        throw new ItemPlacementException($"No door layout for seed {settings.Seed} could be finished (try another seed; last: {why}).");
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
            MusicWriter.Write(data, result.MusicTargetRms, result.Music, directory, progress);
            MusicWriter.WriteTitleCall(data, result.TitleCall, directory, progress);
            if (result.Npcs is { Voices.Count: > 0 })
                progress?.Report("Writing the NPCs' voices...");
            NpcRandomizer.WriteVoices(data, result.Npcs?.Voices ?? [], directory);
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
        // Claire's run starts as a new game does (the opening, the cell); Chris's where he arrives.
        if (result.Doors is { Character: Character.Chris } doors)
        {
            var start = RoomId.ParseBioRand(doors.StartRoom);
            sb.AppendLine($"start={start.Stage},{start.Room},{doors.StartEntrance},{start.Variant},1");
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
