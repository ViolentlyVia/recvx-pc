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

    // Doors (Phase 6): read by the room data filters already.
    public bool RandomDoors { get; init; }
}

public enum ItemMode
{
    Shuffle,
    Custom,
}
