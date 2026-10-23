namespace Cvx.Rando.Data;

/// <summary>The game's items (data/items.json).</summary>
public sealed class ItemCatalog
{
    private readonly Dictionary<int, ItemInfo> _byId;

    public IReadOnlyList<ItemInfo> Items { get; }

    public ItemCatalog(IReadOnlyList<ItemInfo> items)
    {
        Items = items;
        _byId = items.ToDictionary(x => x.Id);
    }

    public ItemInfo? Get(int id) => _byId.GetValueOrDefault(id);

    public string NameOf(int id) => Get(id)?.Name ?? $"Item {id}";
}

public sealed class ItemInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Symbol { get; set; } = "";
    /// <summary>weapon, ammo, heal, inkribbon, key, document, gunpowder, special.</summary>
    public string[] Kinds { get; set; } = [];
    /// <summary><c>itemdata[id].max</c> in the game.</summary>
    public int GameMax { get; set; }
    /// <summary><c>itemdata[id].type</c> flags in the game.</summary>
    public int GameType { get; set; }
    public int Slots { get; set; } = 1;
    /// <summary>Largest sensible amount for one pickup (BioRand's GetMaxAmmoForAmmoType).</summary>
    public int MaxPickup { get; set; }
    public string? WeaponKind { get; set; }
    public int[] Ammo { get; set; } = [];

    public bool Is(string kind) => Kinds.Contains(kind, StringComparer.OrdinalIgnoreCase);
    public bool IsKey => Is("key");
    public bool IsWeapon => Is("weapon");
    public bool IsDocument => Is("document");

    public override string ToString() => $"{Id} {Name}";
}

internal sealed class ItemCatalogFile
{
    public List<ItemInfo> Items { get; set; } = [];
}
