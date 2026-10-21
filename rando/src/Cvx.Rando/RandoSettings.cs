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
