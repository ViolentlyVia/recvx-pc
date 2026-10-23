// Starting inventory after BioRand's ItemRandomiser.RandomizeStartInventory
// (MIT, Copyright (c) 2022-2026 Ted John).

using Cvx.Rando.Data;

namespace Cvx.Rando.Items;

public sealed record InventoryItem(int Type, int Count);

/// <summary>A character's starting items (not counting the special slot), in inventory order.</summary>
public sealed record StartInventory(string Character, IReadOnlyList<InventoryItem> Items)
{
    public const int Slots = 8;

    public IEnumerable<int> Weapons(ItemCatalog items) =>
        Items.Where(i => items.Get(i.Type)?.WeaponKind is "sidearm" or "primary" or "powerful").Select(i => i.Type);

    /// <summary>
    /// Up to two weapons of the chosen kinds (loaded, often with spare ammo; the knife is a
    /// sidearm), then one basic healing item, in 8 slots. Two-slot weapons go first, as the game
    /// sorts them (itemdata type bit 0x100).
    /// </summary>
    public static StartInventory Make(string character, ItemCatalog items, RandoSettings settings, Rng rng)
    {
        var list = new List<InventoryItem>();
        var used = 0;

        bool Add(int type, int count)
        {
            var slots = items.Get(type)?.Slots ?? 1;
            if (used + slots > Slots || count <= 0)
                return false;
            if (slots == 2)
                list.Insert(list.Count(i => (items.Get(i.Type)?.Slots ?? 1) == 2), new InventoryItem(type, count));
            else
                list.Add(new InventoryItem(type, count));
            used += slots;
            return true;
        }

        var weapons = items.Items.Where(i => i.WeaponKind is "sidearm" or "primary" or "powerful").ToList();
        var chosen = new List<ItemInfo>();
        foreach (var choice in new[] { settings.StartWeapon1, settings.StartWeapon2 })
        {
            var kind = choice == WeaponChoice.Random
                ? rng.NextOf([WeaponChoice.None, WeaponChoice.Sidearm, WeaponChoice.Primary, WeaponChoice.Powerful])
                : choice;
            if (kind == WeaponChoice.None)
                continue;
            var name = kind.ToString().ToLowerInvariant();
            var options = weapons.Where(w => w.WeaponKind == name && !chosen.Contains(w)).ToList();
            if (options.Count != 0)
                chosen.Add(rng.NextOf(options));
        }

        foreach (var weapon in chosen)
        {
            if (!Add(weapon.Id, Math.Max(1, weapon.MaxPickup)))
                continue;
            if (weapon.Ammo.Length != 0 && rng.NextProbability(75))
            {
                var ammo = items.Get(rng.NextOf(weapon.Ammo))!;
                var max = Math.Max(1, (int)(ammo.MaxPickup * (settings.AmmoQuantity / 8.0)));
                Add(ammo.Id, rng.Next(1, Math.Min(max * 2, 100) + 1));
            }
        }

        // Healing: one green herb, red + green mix or spray.
        Add(rng.NextWeighted([(21, 0.6), (25, 0.25), (20, 0.15)]), 1);

        return new StartInventory(character, list);
    }
}
