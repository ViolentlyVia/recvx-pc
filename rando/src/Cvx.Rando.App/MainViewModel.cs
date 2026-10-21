using System.Diagnostics;
using Avalonia.Media;
using Cvx.Formats;
using Cvx.Rando.Data;
using Cvx.Rando.Items;
using Cvx.Rando.Music;
using Cvx.Rando.Npcs;
using Cvx.Rando.Setup;

namespace Cvx.Rando.App;

/// <summary>What the view model needs from the window: its dialogs, the folder picker and the clipboard.</summary>
public interface IAppHost
{
    /// <summary>A question with Cancel and <paramref name="ok"/>; true when <paramref name="ok"/> was chosen.</summary>
    Task<bool> Confirm(string title, string body, string ok);
    Task Message(string title, string body, bool warning = false);
    Task<string?> PickFolder(string title, string? start);
    Task<string?> GetClipboardText();
    Task SetClipboardText(string text);
}

/// <summary>The randomizer's window: settings, share code, generate and play.</summary>
public sealed class MainViewModel : ViewModelBase
{
    public const string AppTitle = "CVX Randomizer";

    public static readonly string[] ItemModes = ["Shuffle", "Custom"];
    public static readonly string[] WeaponChoices = Enum.GetNames<WeaponChoice>();
    public static readonly string[] Difficulties = ["Easy", "Normal", "Hard", "Very hard"];
    public static readonly string[] Characters = Enum.GetNames<Character>();
    public static readonly string[] NpcCast = ["Steve", "Rodrigo", "Alfred", "Alexia", "Wesker"];
    public static readonly string[] EnemyKinds = ["Zombies", "Cerberus dogs", "Bats", "Black Widows", "Bandersnatches", "Hunters", "Sweepers"];

    private static readonly IBrush Ok = Brush.Parse("#4fae74"), Warn = Brush.Parse("#d6a54a"), Grey = Brush.Parse("#6c6d75");

    private readonly AppSettings _app;
    private readonly RandoData _data = RandoData.Load();
    private IAppHost? _host;
    private GameFolder? _game;
    private MusicLibrary? _library;
    /// <summary>Games the settings ask music from that the BioRand folder does not have (kept for the share code).</summary>
    private List<string> _missingMusic = [];
    private CharacterLibrary? _characters;
    /// <summary>BioRand characters the settings ask for that the BioRand folder does not have (kept for the share code).</summary>
    private List<string> _missingCharacters = [];
    private bool _loading;
    private bool _busy;
    private string? _status;
    private string _category = "items";
    private bool _editingShareCode;

    public MainViewModel(AppSettings app)
    {
        _app = app;
        Weapons = _data.Items.Items
            .Where(i => i.IsWeapon && i.WeaponKind is { } k && k != "none")
            .OrderBy(i => i.Id)
            .Select(i => new WeaponChip(i.Id, i.Name, Changed))
            .ToList();
        Nav =
        [
            new NavItem(this, "items", "Items"),
            new NavItem(this, "inventory", "Inventory"),
            new NavItem(this, "enemies", "Enemies"),
            new NavItem(this, "doors", "Doors"),
            new NavItem(this, "npcs", "NPCs"),
            new NavItem(this, "music", "Music"),
        ];
        MusicKinds = _data.Music.Categories.Select(c => new MusicKind(c.Name, string.Join(" · ", c.BioRand))).ToList();
        FindGame(app.GameDirectory);
        FindMusic();
        Show(app.Rando);
    }

    public void Attach(IAppHost host) => _host = host;

    public static string Version =>
        typeof(MainViewModel).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";

    public bool IsBusy => _busy;

    // ------------------------------------------------------------------ settings

    private int _seed;
    private string _seedText = "";
    private bool _randomItems, _includeChris, _hiddenKeys, _randomInventory, _sidePack, _noPoison, _randomEnemies, _randomDoors, _randomMusic, _titleCall, _randomNpcs;
    private int _itemMode, _ratioAmmo, _ratioHealth, _ratioInk, _ratioPowder, _ammoQuantity, _weapon1, _weapon2, _difficulty, _character, _doorSegments, _doorSize;
    private string _shareCodeText = "";

    /// <summary>The seed box: digits only, 0 to 999,999,999.</summary>
    public string SeedText
    {
        get => _seedText;
        set
        {
            var digits = new string((value ?? "").Where(char.IsAsciiDigit).ToArray());
            _seedText = digits;
            if (digits.Length != 0)
                _seed = (int)Math.Min(999_999_999, long.Parse(digits.Length > 10 ? digits[..10] : digits));
            if (digits != value)
                Raise();
            Changed();
        }
    }

    /// <summary>The seed box lost focus: show the number it stands for.</summary>
    public void CommitSeedText()
    {
        _seedText = _seed.ToString();
        Raise(nameof(SeedText));
    }

    public void RandomSeed()
    {
        _seed = Random.Shared.Next(1, 1_000_000);
        _seedText = _seed.ToString();
        Changed();
    }

    public bool RandomItems { get => _randomItems; set { if (Set(ref _randomItems, value)) Changed(); } }
    public int ItemModeIndex { get => _itemMode; set { if (Set(ref _itemMode, Math.Max(0, value))) Changed(); } }
    public bool IncludeChris { get => _includeChris; set { if (Set(ref _includeChris, value)) Changed(); } }
    public bool HiddenKeys { get => _hiddenKeys; set { if (Set(ref _hiddenKeys, value)) Changed(); } }
    public int RatioAmmo { get => _ratioAmmo; set { if (Set(ref _ratioAmmo, value)) Changed(); } }
    public int RatioHealth { get => _ratioHealth; set { if (Set(ref _ratioHealth, value)) Changed(); } }
    public int RatioInk { get => _ratioInk; set { if (Set(ref _ratioInk, value)) Changed(); } }
    public int RatioPowder { get => _ratioPowder; set { if (Set(ref _ratioPowder, value)) Changed(); } }
    public double AmmoQuantity { get => _ammoQuantity; set { if (Set(ref _ammoQuantity, Math.Clamp((int)Math.Round(value), 1, 16))) Changed(); } }
    public IReadOnlyList<WeaponChip> Weapons { get; }

    public bool RandomInventory { get => _randomInventory; set { if (Set(ref _randomInventory, value)) Changed(); } }
    public int Weapon1Index { get => _weapon1; set { if (Set(ref _weapon1, Math.Max(0, value))) Changed(); } }
    public int Weapon2Index { get => _weapon2; set { if (Set(ref _weapon2, Math.Max(0, value))) Changed(); } }
    public bool SidePack { get => _sidePack; set { if (Set(ref _sidePack, value)) Changed(); } }
    public bool NoPoison { get => _noPoison; set { if (Set(ref _noPoison, value)) Changed(); } }

    public bool RandomEnemies { get => _randomEnemies; set { if (Set(ref _randomEnemies, value)) Changed(); } }
    public int DifficultyIndex { get => _difficulty; set { if (Set(ref _difficulty, Math.Max(0, value))) Changed(); } }

    public bool RandomDoors { get => _randomDoors; set { if (Set(ref _randomDoors, value)) Changed(); } }
    public int CharacterIndex { get => _character; set { if (Set(ref _character, Math.Max(0, value))) Changed(); } }
    public int DoorSegments { get => _doorSegments; set { if (Set(ref _doorSegments, value)) Changed(); } }
    public double DoorSize { get => _doorSize; set { if (Set(ref _doorSize, Math.Clamp((int)Math.Round(value), 1, 8))) Changed(); } }

    public bool RandomMusic { get => _randomMusic; set { if (Set(ref _randomMusic, value)) Changed(); } }
    public bool RandomTitleCall { get => _titleCall; set { if (Set(ref _titleCall, value)) Changed(); } }
    public bool RandomNpcs { get => _randomNpcs; set { if (Set(ref _randomNpcs, value)) Changed(); } }
    /// <summary>This game and each game of the BioRand folder with music.</summary>
    public IReadOnlyList<MusicGameChip> MusicGames { get; private set; } = [];
    public IReadOnlyList<MusicKind> MusicKinds { get; }

    // ------------------------------------------------------------------ derived state (enable rules = the old SettingsChanged)

    private bool Chris => _character == (int)Character.Chris;
    private bool Custom => _randomItems && _itemMode == (int)ItemMode.Custom;

    /// <summary>Random doors always randomize items: the switch shows on and cannot be turned off.</summary>
    public bool ItemsMasterEnabled => !_randomDoors;
    public bool ItemsOn => _randomItems;
    public bool DoorsLock => _randomDoors;
    public bool IncludeChrisEnabled => _randomItems && !_randomDoors;
    public bool CustomOn => Custom;
    public string ModeNote => _itemMode == (int)ItemMode.Shuffle ? "The game's own items, moved around." : "New items from the mix below.";
    public string ChrisNote => _randomDoors ? "Random doors: one character plays." : "His half gets its own key items. Off: it plays as normal.";
    public string CustomNote => Custom ? "Relative weights, 0–32" : "Custom mode only";
    public int[] MixWeights => [_ratioAmmo, _ratioHealth, _ratioInk, _ratioPowder];
    private string Pct(int v) => $"{Math.Round(100.0 * v / Math.Max(1, _ratioAmmo + _ratioHealth + _ratioInk + _ratioPowder))}%";
    public string AmmoPct => Pct(_ratioAmmo);
    public string HealthPct => Pct(_ratioHealth);
    public string InkPct => Pct(_ratioInk);
    public string PowderPct => Pct(_ratioPowder);
    public string AmmoQuantityText => _ammoQuantity.ToString();
    public string WeaponsNote => Weapons.Count(w => w.IsOn) is var n and > 0 ? $"{n} of {Weapons.Count} allowed" : "None picked: all can appear";

    public string CharNote => Chris
        ? "Chris starts where he arrives on Rockfort Island. At most three segments."
        : "Claire starts in the second room of the game.";
    public int MaxSegments => Chris ? 3 : 4;
    public IReadOnlyList<string> SegmentBlocks => Enumerable.Range(0, _doorSegments).Select(i => i < _doorSegments - 1 ? "Story door" : "Last room").ToList();
    public string DoorSizeText => _doorSize == 8 ? "Every room it can" : $"{_doorSize}/8 of the rooms";

    public string ItemsState => OnOff(_randomItems);
    public string EnemiesState => OnOff(_randomEnemies);
    public string DoorsState => OnOff(_randomDoors);
    public string MusicState => OnOff(_randomMusic);
    public string NpcsState => OnOff(_randomNpcs);
    private static string OnOff(bool on) => on ? "On" : "Off";

    // ------------------------------------------------------------------ categories (sidebar)

    public IReadOnlyList<NavItem> Nav { get; }

    public string Category
    {
        get => _category;
        set
        {
            if (!Set(ref _category, value))
                return;
            Raise("");
            foreach (var n in Nav)
                n.Refresh();
        }
    }

    public bool ShowItems => _category == "items";
    public bool ShowInventory => _category == "inventory";
    public bool ShowEnemies => _category == "enemies";
    public bool ShowDoors => _category == "doors";
    public bool ShowMusic => _category == "music";
    public bool ShowNpcs => _category == "npcs";

    internal string Detail(string key) => key switch
    {
        "items" => !_randomItems ? "Original placement"
            : (_itemMode == (int)ItemMode.Shuffle ? "Game's own items" : "New item mix") + (_randomDoors ? " · forced by doors" : ""),
        "inventory" => _randomInventory ? $"{WeaponChoices[_weapon1]} + {WeaponChoices[_weapon2]}"
            : _sidePack || _noPoison ? "Default weapons" : "Default start",
        "enemies" => _randomEnemies ? $"{Difficulties[_difficulty]} difficulty" : "Original enemies",
        "doors" => _randomDoors ? $"{Characters[_character]}, {_doorSegments} segment{(_doorSegments > 1 ? "s" : "")}" : "Original doors",
        "npcs" => !_randomNpcs ? "Original cast"
            : NpcCharacterCount == 0 ? "Looks and voices shuffled"
            : $"Shuffled + {NpcCharacterCount} BioRand character{(NpcCharacterCount > 1 ? "s" : "")}",
        _ => (!_randomMusic ? "Original music"
            : OtherMusicGames == 0 ? "Shuffled by kind"
            : $"{(MusicGames[0].IsOn ? "CV + " : "")}{OtherMusicGames} other game{(OtherMusicGames > 1 ? "s" : "")}")
            + (_titleCall ? " · title call" : ""),
    };

    private int OtherMusicGames => MusicGames.Count(g => g.IsOn && g.Id != MusicLibrary.OwnGame);
    private int NpcCharacterCount => NpcCharacters.Count(c => c.IsOn);

    internal bool Master(string key) => key switch
    {
        "items" => _randomItems,
        "inventory" => _randomInventory,
        "enemies" => _randomEnemies,
        "doors" => _randomDoors,
        "npcs" => _randomNpcs,
        _ => _randomMusic,
    };

    internal void SetMaster(string key, bool on)
    {
        switch (key)
        {
            case "items": if (ItemsMasterEnabled) RandomItems = on; break;
            case "inventory": RandomInventory = on; break;
            case "enemies": RandomEnemies = on; break;
            case "doors": RandomDoors = on; break;
            case "npcs": RandomNpcs = on; break;
            default: RandomMusic = on; break;
        }
    }

    // ------------------------------------------------------------------ settings <-> state

    private void Show(RandoSettings s)
    {
        _loading = true;
        _seed = Math.Clamp(s.Seed, 0, 999_999_999);
        _seedText = _seed.ToString();
        _randomItems = s.RandomItems;
        _itemMode = (int)s.ItemMode;
        _includeChris = s.IncludeChris;
        _hiddenKeys = s.HiddenKeyItems;
        _ratioAmmo = Math.Clamp(s.RatioAmmo, 0, 32);
        _ratioHealth = Math.Clamp(s.RatioHealth, 0, 32);
        _ratioInk = Math.Clamp(s.RatioInkRibbons, 0, 32);
        _ratioPowder = Math.Clamp(s.RatioGunpowder, 0, 32);
        _ammoQuantity = Math.Clamp(s.AmmoQuantity, 1, 16);
        foreach (var w in Weapons)
            w.Load(s.Weapons.Contains(w.Id));
        _randomInventory = s.RandomInventory;
        _weapon1 = (int)s.StartWeapon1;
        _weapon2 = (int)s.StartWeapon2;
        _sidePack = s.StartSidePack;
        _noPoison = s.NoNosferatuPoison;
        _randomEnemies = s.RandomEnemies;
        _difficulty = Math.Clamp(s.EnemyDifficulty, 0, 3);
        _randomDoors = s.RandomDoors;
        _character = (int)s.DoorCharacter;
        _doorSegments = Math.Clamp(s.DoorSegments, 1, 4);
        _doorSize = Math.Clamp(s.DoorSegmentSize, 1, 8);
        _randomMusic = s.RandomMusic;
        _titleCall = s.RandomTitleCall;
        _randomNpcs = s.RandomNpcs;
        var games = s.MusicGames.Select(g => g.ToLowerInvariant()).ToHashSet();
        foreach (var chip in MusicGames)
            chip.Load(games.Contains(chip.Id));
        _missingMusic = games.Where(g => MusicGames.All(c => c.Id != g)).Order(StringComparer.Ordinal).ToList();
        var characters = s.NpcCharacters.Select(c => c.ToLowerInvariant()).ToHashSet();
        foreach (var chip in NpcCharacters)
            chip.Load(characters.Contains(chip.Id));
        _missingCharacters = characters.Where(c => NpcCharacters.All(x => x.Id != c)).Order(StringComparer.Ordinal).ToList();
        _loading = false;
        Update();
    }

    private RandoSettings Read() => new()
    {
        Seed = _seed,
        RandomItems = _randomItems || _randomDoors,
        ItemMode = _itemMode == (int)ItemMode.Custom ? ItemMode.Custom : ItemMode.Shuffle,
        IncludeChris = _includeChris,
        HiddenKeyItems = _hiddenKeys,
        RatioAmmo = _ratioAmmo,
        RatioHealth = _ratioHealth,
        RatioInkRibbons = _ratioInk,
        RatioGunpowder = _ratioPowder,
        AmmoQuantity = _ammoQuantity,
        Weapons = Weapons.Where(w => w.IsOn).Select(w => w.Id).Order().ToArray(),
        RandomInventory = _randomInventory,
        StartWeapon1 = (WeaponChoice)_weapon1,
        StartWeapon2 = (WeaponChoice)_weapon2,
        StartSidePack = _sidePack,
        NoNosferatuPoison = _noPoison,
        RandomEnemies = _randomEnemies,
        EnemyDifficulty = _difficulty,
        RandomDoors = _randomDoors,
        DoorCharacter = (Character)_character,
        DoorSegments = _doorSegments,
        DoorSegmentSize = _doorSize,
        RandomMusic = _randomMusic,
        RandomTitleCall = _titleCall,
        RandomNpcs = _randomNpcs,
        // this game first, then the others by name: one order, one share code
        MusicGames = MusicGames.Where(g => g.IsOn).Select(g => g.Id).Concat(_missingMusic)
            .OrderBy(g => g == MusicLibrary.OwnGame ? 0 : 1).ThenBy(g => g, StringComparer.Ordinal).ToArray(),
        NpcCharacters = NpcCharacters.Where(c => c.IsOn).Select(c => c.Id).Concat(_missingCharacters).Order(StringComparer.Ordinal).ToArray(),
    };

    /// <summary>A setting was changed by the player: the last action's message gives way to the notice.</summary>
    private void Changed()
    {
        if (_loading)
            return;
        _status = null;
        Update();
    }

    private void Update()
    {
        // random doors always randomize items
        if (_randomDoors)
            _randomItems = true;
        // Chris has at most three segments
        _doorSegments = Math.Clamp(_doorSegments, 1, MaxSegments);
        if (!_editingShareCode)
            _shareCodeText = ShareCode.Encode(Read());
        Raise("");
        foreach (var n in Nav)
            n.Refresh();
    }

    // ------------------------------------------------------------------ share code

    public string ShareCodeText
    {
        get => _shareCodeText;
        set => Set(ref _shareCodeText, value ?? "");
    }

    /// <summary>The share code box has focus: typing in it does not get overwritten.</summary>
    public void EditShareCode(bool editing)
    {
        _editingShareCode = editing;
        if (!editing && _shareCodeText != ShareCode.Encode(Read()))
            LoadShareCode(_shareCodeText);
    }

    private void LoadShareCode(string text)
    {
        if (ShareCode.TryDecode(text, out var settings, out var error))
        {
            Show(settings!);
            SetStatus($"Loaded the settings of seed {settings!.Seed}.");
        }
        else
        {
            _shareCodeText = ShareCode.Encode(Read());
            SetStatus(error ?? "That share code is not valid.");
        }
    }

    public async Task CopyShareCode()
    {
        if (_host != null)
            await _host.SetClipboardText(_shareCodeText);
        SetStatus("Share code copied.");
    }

    public async Task PasteShareCode()
    {
        if (_host != null)
            LoadShareCode(await _host.GetClipboardText() ?? "");
    }

    // ------------------------------------------------------------------ game folder

    public string GameTitle =>
        _game == null ? "Game folder not found"
        : !_game.HasData ? "Game data not set up"
        : _game.ActiveSeed is { } seed ? $"Randomizer on · seed {seed}"
        : "Randomizer off";

    public IBrush GameDot => _game is not { HasData: true } ? Warn : _game.ActiveSeed != null ? Ok : Grey;
    public string GamePath => _game?.Directory ?? "";
    public string GameTooltip => _game?.Directory ?? $"Choose the folder with {GameFolder.ExeName}.";
    public bool GameActive => _game?.ActiveSeed != null;

    // ------------------------------------------------------------------ other games' music (BioRand)

    public string MusicFolderText => _library is { } l
        ? $"{l.Directory}: {l.Games.Count} games, {l.Tracks.Count} tracks."
        : "Not found. Choose BioRand's folder (the one with datapacks) to add music from other games.";
    public bool MusicFolderFound => _library != null;
    public string MusicGamesNote => MusicGames.Any(g => g.IsOn) ? "Tracks swap with others of their kind from these games." : "None picked: Code: Veronica's own.";
    public string MissingMusicText => _missingMusic.Count == 0 ? ""
        : $"Not in your BioRand folder, left out: {string.Join(", ", _missingMusic.Select(MusicLibrary.DisplayName))}.";
    public bool HasMissingMusic => _missingMusic.Count != 0;
    public string TitleCallNote => _library is { TitleCalls.Count: > 0 } l
        ? $"At the title screen, one of BioRand's {l.TitleCalls.Count} title calls (recorded by its community) says the name instead of the game's own. The seed picks it."
        : "Needs BioRand's folder: its base datapack has the title calls. Without it the game's own plays.";
    public bool TitleCallsFound => _library is { TitleCalls.Count: > 0 };

    // ------------------------------------------------------------------ BioRand's characters (NPC looks)

    /// <summary>Each character of the BioRand folder that can be an NPC look.</summary>
    public IReadOnlyList<CharacterChip> NpcCharacters { get; private set; } = [];
    public bool CharactersFound => _characters != null;
    public string CharactersNote => _characters is { } l
        ? $"{l.Characters.Count} characters in BioRand's folder. The ones picked join the looks the NPCs can take (an NPC who takes one keeps their own voice)."
        : "Needs BioRand's folder (the one with datapacks): its characters can be NPC looks too.";
    public string MissingCharactersText => _missingCharacters.Count == 0 ? ""
        : $"Not in your BioRand folder, left out: {string.Join(", ", _missingCharacters.Select(CharacterLibrary.DisplayName))}.";
    public bool HasMissingCharacters => _missingCharacters.Count != 0;

    public void PickRe2Characters() => PickCharacters(c => c.Id.EndsWith(".re2", StringComparison.Ordinal));
    public void PickAllCharacters() => PickCharacters(_ => true);
    public void PickNoCharacters() => PickCharacters(_ => false);

    private void PickCharacters(Func<CharacterChip, bool> on)
    {
        _loading = true;
        foreach (var chip in NpcCharacters)
            chip.Load(on(chip));
        _loading = false;
        Changed();
    }

    /// <summary>BioRand's music and characters: the folder chosen before, or a "biorand" folder next to or above the program or the game.</summary>
    private void FindMusic()
    {
        _characters = CharacterLibrary.Find(_app.BioRandDirectory, AppContext.BaseDirectory, _game?.Directory);
        var picked = NpcCharacters.Where(c => c.IsOn).Select(c => c.Id).Concat(_missingCharacters).ToHashSet();
        NpcCharacters = _characters?.Characters.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => new CharacterChip(c.Id, c.Name, $"{c.Id} ({Path.GetFileName(c.Source)})", Changed)).ToList() ?? [];
        foreach (var chip in NpcCharacters)
            chip.Load(picked.Contains(chip.Id));
        _missingCharacters = picked.Where(c => NpcCharacters.All(x => x.Id != c)).Order(StringComparer.Ordinal).ToList();

        _library = MusicLibrary.Find(_app.BioRandDirectory, AppContext.BaseDirectory, _game?.Directory);
        var counts = _library?.Tracks.GroupBy(t => t.Game).ToDictionary(g => g.Key, g => g.Count()) ?? [];
        var on = MusicGames.Where(g => g.IsOn).Select(g => g.Id).Concat(_missingMusic).ToHashSet();
        if (MusicGames.Count == 0)
            on.Add(MusicLibrary.OwnGame);
        MusicGames =
        [
            new MusicGameChip(MusicLibrary.OwnGame, MusicLibrary.DisplayName(MusicLibrary.OwnGame), "This game's own music", Changed),
            .. counts.Keys.Order(StringComparer.Ordinal).Select(g =>
                new MusicGameChip(g, MusicLibrary.DisplayName(g), $"{counts[g]} tracks in BioRand", Changed)),
        ];
        foreach (var chip in MusicGames)
            chip.Load(on.Contains(chip.Id));
        _missingMusic = on.Where(g => MusicGames.All(c => c.Id != g)).Order(StringComparer.Ordinal).ToList();
    }

    public async Task ChooseMusicFolder()
    {
        if (_host == null || _busy)
            return;
        var path = await _host.PickFolder("BioRand's folder", _library?.Directory);
        if (path == null)
            return;
        if (MusicLibrary.Open(path) == null)
        {
            await _host.Message("No music in that folder", "BioRand's music is in its datapacks folder (classic.zip, modern.zip...). Choose the folder that has it.");
            return;
        }
        _app.BioRandDirectory = path;
        _app.Save();
        FindMusic();
        Update();
    }

    private void FindGame(string? remembered)
    {
        var dev = GameData.FindDefaultDirectory() is { } data ? Path.GetDirectoryName(Path.GetFullPath(data)) : null;
        _game = GameFolder.Find(remembered, AppContext.BaseDirectory) ?? GameFolder.Find(dev, AppContext.BaseDirectory);
        Raise("");
    }

    public async Task ChooseGame()
    {
        if (_host == null || _busy)
            return;
        var path = await _host.PickFolder($"The folder with {GameFolder.ExeName}", _game?.Directory);
        if (path == null)
            return;
        var folder = new GameFolder(path);
        if (!folder.HasExe)
        {
            await _host.Message("Not the game folder", $"There is no {GameFolder.ExeName} in that folder.");
            return;
        }
        _game = folder;
        _app.GameDirectory = folder.Directory;
        _app.Save();
        Raise("");
    }

    // ------------------------------------------------------------------ footer: flow, status, buttons

    private bool Ready => _game is { HasData: true };

    /// <summary>The active seed's settings match the options: compared by share code.</summary>
    private bool UpToDate => _game?.ActiveSeed != null && _app.LastGeneratedCode == ShareCode.Encode(Read());

    /// <summary>The last generated seed is the one in the game, so its options are known.</summary>
    private bool KnownCode => _game?.ActiveSeed is { } active && _app.LastGeneratedCode is { } code &&
                              ShareCode.TryDecode(code, out var s, out _) && s!.Seed.ToString() == active;

    public string FlowOptions => !GameActive ? "Not generated" : UpToDate ? "Match seed" : "Changed";
    public bool FlowOptionsWarn => GameActive && !UpToDate;
    public string FlowSeed => _game?.ActiveSeed is { } seed ? $"rando\\{seed}" : "None yet";
    public string FlowGame => GameActive ? "Plays the seed" : "Plays normally";

    private string Notice =>
        _game == null ? $"Choose the folder with {GameFolder.ExeName}."
        : !_game.HasData ? $"Run {GameFolder.ConfigName} first."
        : _game.ActiveSeed is not { } active ? $"No seed yet. Generate writes rando\\{_seed} and switches the game to it."
        : UpToDate ? $"Seed {active} is up to date with these options."
        : KnownCode ? $"Options changed since seed {active}. Generate to apply."
        : $"Seed {active} is in the game. Generate replaces it.";

    private bool NoticeWarn => !Ready || (GameActive && !UpToDate && KnownCode);

    public string StatusText => _status ?? Notice;
    public bool StatusWarn => _status == null && NoticeWarn;

    public bool CanGenerate => Ready && !_busy;
    public bool CanPlayNormal => !_busy && _game?.ActiveMod != null;
    public bool CanOpen => !_busy && _game != null;
    public string GeneratePlayLabel => _busy ? "Generating…" : "Generate and play";

    private void SetStatus(string text)
    {
        _status = text;
        Raise(nameof(StatusText));
        Raise(nameof(StatusWarn));
    }

    // ------------------------------------------------------------------ actions

    /// <summary>
    /// Writes the seed and points the game at it. One seed at a time: once it is written, the
    /// other seed folders and the random run's saves are deleted (asked first).
    /// </summary>
    public async Task Generate(bool play)
    {
        if (_host == null || _busy || _game is not { HasData: true } game)
            return;
        var settings = Read();
        var target = game.SeedDirectory(settings.Seed);
        if (GameRunning())
        {
            await _host.Message("The game is running", "Close it first: a new seed replaces the current one and its saves.");
            return;
        }
        var replaces = game.SeedFolders().Count != 0 || game.HasRandomSaves;
        if (replaces && !await _host.Confirm(
                game.ActiveSeed is { } active ? $"Replace seed {active}?" : "Replace the current seed?",
                "Its folder and the random run's saves (saves_rando) will be deleted. Your own saves are not touched.",
                "Replace and generate"))
            return;

        _busy = true;
        Raise("");
        var progress = new Progress<string>(SetStatus);
        var watch = Stopwatch.StartNew();
        try
        {
            var (result, stuck) = await Task.Run(() =>
            {
                using var rooms = RoomArchive.Open(new GameData(game.DataDirectory));
                var r = Generator.Generate(settings, _data, rooms, progress, _library, _characters);
                Generator.WriteMod(r, rooms, target, progress);
                var old = game.DeleteOtherSeeds(settings.Seed);
                game.ClearRandomSaves();
                return (r, old);
            });
            game.Play(settings.Seed);
            _app.Rando = settings;
            _app.GameDirectory = game.Directory;
            _app.LastGeneratedCode = ShareCode.Encode(settings);
            _app.Save();
            SetStatus($"Seed {settings.Seed} is ready ({watch.Elapsed.TotalSeconds:0} s){(result.Enemies.Count != 0 ? $", enemies changed in {result.Enemies.Count} rooms" : "")}" +
                      (stuck.Count != 0 ? $"; {stuck.Count} old seed folder(s) could not be deleted." : "."));
            if (play)
                await StartGame(game);
        }
        catch (ItemPlacementException ex)
        {
            SetStatus("This seed could not be made.");
            await _host.Message("This seed could not be made", $"{ex.Message}\n\nTry another seed or other settings.");
        }
        catch (IOException ex)
        {
            SetStatus("Could not write the seed.");
            await _host.Message("Could not write the seed", $"Could not write the seed's files:\n{ex.Message}\n\nIs the game running? Close it and try again.", warning: true);
        }
        catch (Exception ex) when (ex is InvalidDataException or UnauthorizedAccessException)
        {
            SetStatus("Something went wrong.");
            await _host.Message("Something went wrong", ex.Message, warning: true);
        }
        finally
        {
            _busy = false;
            Raise("");
        }
    }

    private static bool GameRunning() => Process.GetProcessesByName("recvx").Length != 0;

    private async Task StartGame(GameFolder game)
    {
        try
        {
            Process.Start(new ProcessStartInfo(game.ExePath) { WorkingDirectory = game.Directory, UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            if (_host != null)
                await _host.Message("Could not start the game", $"Could not start the game:\n{ex.Message}", warning: true);
        }
    }

    public void PlayNormal()
    {
        if (_busy)
            return;
        _game?.PlayNormal();
        _status = "The game plays normally again, with your own saves.";
        Raise("");
    }

    private string? CurrentSeedFolder() =>
        _game == null ? null
        : _game.ActiveMod is { } mod && Directory.Exists(mod) ? mod
        : Directory.Exists(_game.SeedDirectory(_seed)) ? _game.SeedDirectory(_seed)
        : null;

    public void OpenFolder()
    {
        if (_game == null || _busy)
            return;
        var folder = CurrentSeedFolder() ?? _game.Directory;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = false });
    }

    public async Task OpenSpoiler()
    {
        if (_host == null || _busy)
            return;
        var spoiler = CurrentSeedFolder() is { } folder ? Path.Combine(folder, "spoiler.txt") : null;
        if (spoiler == null || !File.Exists(spoiler))
        {
            SetStatus("No spoiler log yet: generate a seed first.");
            return;
        }
        if (await _host.Confirm("Spoiler log", "The spoiler log shows where everything is. Open it?", "Open it"))
            Process.Start(new ProcessStartInfo(spoiler) { UseShellExecute = true });
    }

    /// <summary>The window is closing: remember the options.</summary>
    public void SaveOnClose(WindowPlacement? placement)
    {
        _app.Rando = Read();
        if (placement != null)
            _app.Window = placement;
        _app.Save();
    }
}

/// <summary>A sidebar row: the category, a line on what it does now, and its master switch.</summary>
public sealed class NavItem(MainViewModel owner, string key, string label) : ViewModelBase
{
    public string Key => key;
    public string Label => label;
    public string Detail => owner.Detail(key);
    public bool IsSelected => owner.Category == key;
    public bool On { get => owner.Master(key); set => owner.SetMaster(key, value); }
    public bool CanToggle => key != "items" || owner.ItemsMasterEnabled;
    public void Select() => owner.Category = key;
    internal void Refresh() => Raise("");
}

/// <summary>A weapon chip in the custom item mix.</summary>
public sealed class WeaponChip(int id, string name, Action changed) : ViewModelBase
{
    private bool _on;
    public int Id => id;
    public string Name => name;
    public bool IsOn { get => _on; set { if (Set(ref _on, value)) changed(); } }
    internal void Load(bool on) => Set(ref _on, on, nameof(IsOn));
}

/// <summary>A game whose music may play (Music pane).</summary>
public sealed class MusicGameChip(string id, string name, string tip, Action changed) : ViewModelBase
{
    private bool _on;
    public string Id => id;
    public string Name => name;
    public string Tip => tip;
    public bool IsOn { get => _on; set { if (Set(ref _on, value)) changed(); } }
    internal void Load(bool on) => Set(ref _on, on, nameof(IsOn));
}

/// <summary>One of BioRand's characters that may be an NPC look (NPCs pane).</summary>
public sealed class CharacterChip(string id, string name, string tip, Action changed) : ViewModelBase
{
    private bool _on;
    public string Id => id;
    public string Name => name;
    public string Tip => tip;
    public bool IsOn { get => _on; set { if (Set(ref _on, value)) changed(); } }
    internal void Load(bool on) => Set(ref _on, on, nameof(IsOn));
}

/// <summary>A kind of music and BioRand's tags that can take its place.</summary>
public sealed record MusicKind(string Name, string Tags);
