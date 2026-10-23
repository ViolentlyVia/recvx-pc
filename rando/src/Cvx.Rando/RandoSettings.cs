namespace Cvx.Rando;

/// <summary>Everything that decides a randomized game (with <see cref="Seed"/>).</summary>
public sealed record RandoSettings
{
    public int Seed { get; init; }

    // Items
    public bool RandomItems { get; init; } = true;

    /// <summary>
    /// Shuffle: the game's own items, moved around. Custom: new items from the ratios below
    /// (key items always keep their logic).
    /// </summary>
    public ItemMode ItemMode { get; init; } = ItemMode.Shuffle;

    /// <summary>Randomize Chris's half too (his own inventory from the handover on).</summary>
    public bool IncludeChris { get; init; } = true;

    /// <summary>Let key items go to pickups marked low priority (out-of-the-way spots).</summary>
    public bool HiddenKeyItems { get; init; }

    // Custom item pool ratios (relative weights) and ammo amount (1-16; 8 = the game's amounts).
    public int RatioAmmo { get; init; } = 16;
    public int RatioHealth { get; init; } = 10;
    public int RatioInkRibbons { get; init; } = 3;
    public int RatioGunpowder { get; init; } = 2;
    public int AmmoQuantity { get; init; } = 8;

    /// <summary>Weapons that may appear in custom mode (item ids); empty = all.</summary>
    public int[] Weapons { get; init; } = [];

    // Starting inventory (Claire's and Chris's, each made separately)
    /// <summary>Replace the starting items: the weapons below (the knife is a sidearm), maybe ammo, and one healing item.</summary>
    public bool RandomInventory { get; init; }
    public WeaponChoice StartWeapon1 { get; init; } = WeaponChoice.Sidearm;
    public WeaponChoice StartWeapon2 { get; init; } = WeaponChoice.Random;
    /// <summary>Claire and Chris start with the side pack (the bigger inventory).</summary>
    public bool StartSidePack { get; init; }

    // Enemies
    /// <summary>Replace the ordinary enemies of rooms with other types (copied from other rooms).</summary>
    public bool RandomEnemies { get; init; }
    /// <summary>0 easy, 1 normal, 2 hard, 3 very hard: which types come up and how many may share a room.</summary>
    public int EnemyDifficulty { get; init; } = 1;
    /// <summary>
    /// How often each kind comes up (data/enemies.json's names, 0 = never to 7; a kind left out
    /// has its default: 4, or 0 for an untested kind). Empty: the difficulty's own mix (each
    /// kind weighted by its difficulty), as before; with ratios the difficulty only sets how
    /// many of a kind a room may hold, as in BioRand.
    /// </summary>
    public Dictionary<string, int> EnemyRatios { get; init; } = [];
    /// <summary>
    /// BioRand's random enemy placements: rooms get a random number of enemies in random places
    /// on their floor, and some rooms none. Off: enemies keep their places and number.
    /// </summary>
    public bool RandomEnemyPlacements { get; init; }
    /// <summary>Random placements: the average number of enemies in a room that has them, less one (BioRand's slider, 0-15).</summary>
    public int EnemyQuantity { get; init; } = 2;
    /// <summary>Random placements: how many of the rooms that can have enemies have them, in sevenths (BioRand's slider, 0-7).</summary>
    public int EnemyRooms { get; init; } = 5;
    /// <summary>Random placements: save rooms can get enemies too.</summary>
    public bool EnemiesInSaveRooms { get; init; }

    // Music
    /// <summary>Swap music tracks within their category (area, save room, tension, boss).</summary>
    public bool RandomMusic { get; init; }
    /// <summary>
    /// The games whose music the shuffle draws from: "recv" (this game's own) and BioRand's
    /// (re2, sh1...: the folders of data/bgm in its datapacks, from the player's BioRand folder).
    /// </summary>
    public string[] MusicGames { get; init; } = ["recv"];
    /// <summary>NPCs (Steve, Alfred, Alexia, Wesker, Rodrigo) take each other's look and voice; Claire and Chris stay.</summary>
    public bool RandomNpcs { get; init; }
    /// <summary>
    /// BioRand's characters that may be NPC looks too (their ids, e.g. "leon.re2": the folders of
    /// data/re2/pld0 and pld1 in its datapacks, from the player's BioRand folder). Empty: the
    /// NPCs only take each other's looks.
    /// </summary>
    public string[] NpcCharacters { get; init; } = [];
    /// <summary>
    /// Claire and Chris take the look (cutscene models) and voice of BioRand characters
    /// (<see cref="ClaireLook"/>, <see cref="ChrisLook"/>), from the player's BioRand folder.
    /// </summary>
    public bool RandomPlayers { get; init; }
    /// <summary>Who Claire becomes: a BioRand character's id (e.g. "leon.re2"), or empty for a random one.</summary>
    public string ClaireLook { get; init; } = "";
    /// <summary>Who Chris becomes: a BioRand character's id, or empty for a random one (not Claire's).</summary>
    public string ChrisLook { get; init; } = "";
    /// <summary>The title call is one of BioRand's (data/title), picked by the seed.</summary>
    public bool RandomTitleCall { get; init; }

    /// <summary>Nosferatu's attack never poisons (BioRand does this with random doors).</summary>
    public bool NoNosferatuPoison { get; init; }

    // Doors (Phase 6): read by the room data filters already.
    public bool RandomDoors { get; init; }
    /// <summary>
    /// Random doors: how many parts the run has (1-4). Each part but the last ends at a one-way
    /// story door (the plane, the switch to Chris, the handover), the last at the end of the game.
    /// </summary>
    public int DoorSegments { get; init; } = 4;
    /// <summary>Random doors: how much of the game the run uses, in eighths (1-8; 8 = every room it can).</summary>
    public int DoorSegmentSize { get; init; } = 8;
    /// <summary>Random doors: who plays the whole run (no switching between characters).</summary>
    public Character DoorCharacter { get; init; } = Character.Claire;
}

public enum Character
{
    Claire,
    Chris,
}

public enum ItemMode
{
    Shuffle,
    Custom,
}

public enum WeaponChoice
{
    None,
    Random,
    Sidearm,
    Primary,
    Powerful,
}
