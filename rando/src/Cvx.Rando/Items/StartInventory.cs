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
    /// A knife, up to two weapons of the chosen kinds (loaded, often with spare ammo), then
    /// healing items and maybe ink ribbons, in 8 slots. Two-slot weapons go first, as the game
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

        Add(8, 1);   // Combat Knife

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

        // Healing: up to four of herbs, mixed herbs and sprays.
        var healing = rng.Shuffle(new[] { 20, 21, 22, 23, 24, 25, 26, 27, 28, 29 });
        for (var i = 0; i < 4; i++)
        {
            if (rng.NextProbability(50))
                Add(healing[i], 1);
        }

        if (rng.NextProbability(50))
            Add(31, rng.Next(1, 3));   // Ink Ribbon

        return new StartInventory(character, list);
    }
}
