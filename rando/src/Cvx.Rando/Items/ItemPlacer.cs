// Key item placement after BioRand's ItemRandomiser (MIT, Copyright (c) 2022-2026 Ted John),
// rewritten for Code: Veronica:
// - Claire and Chris have separate inventories; the logic starts over at Chris's handover.
// - Keys a half needs but has no pickup of are handed over by the game (events, the other
//   character) and left alone.
// - Pickups that keep their item (fixed ones...) hand it over when reached; the logic waits for
//   them instead of placing copies.
// - Chris's keys only go into pickups that are his alone: Claire may take a shared one first.

using Cvx.Rando.Data;

namespace Cvx.Rando.Items;

public sealed record ItemAssignment(ItemLocation Location, int Type, bool IsKey);

public sealed class ItemPlacementResult
{
    public required IReadOnlyDictionary<int, ItemAssignment> ByGlobalId { get; init; }
    /// <summary>Key items in the order they were placed, as the logic expects them to be found.</summary>
    public required IReadOnlyList<ItemAssignment> KeyOrder { get; init; }
    /// <summary>Keys the logic assumes the game hands over itself (no pickup of them in that half).</summary>
    public required IReadOnlyList<(string Half, int Type)> GivenKeys { get; init; }
    public required IReadOnlyList<string> Log { get; init; }
}

public sealed class ItemPlacementException(string message) : Exception(message);

public sealed class ItemPlacer
{
    // Claire starts with the lighter (special slot); Chris starts with no key items.
    public static readonly int[] ClaireStartKeys = [55];
    public static readonly int[] ChrisStartKeys = [];

    private readonly ItemGraph _graph;
    private readonly ItemCatalog _items;
    private readonly RandoSettings _settings;
    private readonly Rng _rng;
    private readonly List<string> _log = [];

    private readonly Dictionary<int, ItemAssignment> _assigned = [];
    private readonly List<ItemAssignment> _keyOrder = [];
    private readonly HashSet<int> _have = [];
    private readonly HashSet<RoomNode> _visited = [];
    private readonly HashSet<int> _keyTypes;
    private readonly HashSet<int> _chrisKeys;
    private readonly List<(string, int)> _givenKeys = [];
    private readonly IReadOnlyCollection<int> _startWeapons;
    private bool _chrisHalf;

    /// <param name="startWeapons">Weapons Claire starts with: in custom mode they are not placed again (their ammo still is).</param>
    public ItemPlacer(ItemGraph graph, ItemCatalog items, RandoSettings settings, Rng rng, IReadOnlyCollection<int>? startWeapons = null)
    {
        _startWeapons = startWeapons ?? [];
        _graph = graph;
        _items = items;
        _settings = settings;
        _rng = rng;
        _keyTypes = RequiredKeys(graph.Rooms.Values).ToHashSet();
        _chrisKeys = RequiredKeys(graph.Rooms.Values.Where(r => r.IsChris)).ToHashSet();
    }

    public ItemPlacementResult Place()
    {
        PlaceKeys();
        FillOthers();
        return new ItemPlacementResult
        {
            ByGlobalId = _assigned,
            KeyOrder = _keyOrder,
            GivenKeys = _givenKeys,
            Log = _log,
        };
    }

    public static IEnumerable<int> RequiredKeys(IEnumerable<RoomNode> rooms) =>
        rooms.SelectMany(r => r.Requires
                .Concat(r.Edges.SelectMany(e => e.Requires))
                .Concat(r.Items.SelectMany(i => i.Requires)))
            .Distinct();

    private bool IsKey(int type) => _keyTypes.Contains(type);

    /// <summary>
    /// Pickups that keep their own item: fixed ones; Chris's when his half is not randomized;
    /// key items no requirement mentions (the game may need them for a puzzle the data does not
    /// describe); and pickups Claire and Chris share whose key Chris needs (that is where he
    /// gets it in the normal game).
    /// </summary>
    public bool KeepsOriginal(ItemLocation l) =>
        l.Priority == ItemPriority.Fixed ||
        (!_settings.IncludeChris && l.ChrisOnly) ||
        (_items.Get(l.OriginalType)?.IsKey == true && !IsKey(l.OriginalType)) ||
        (_settings.IncludeChris && l.InChris && l.InClaire && _chrisKeys.Contains(l.OriginalType));

    // ---------------------------------------------------------------- key items

    private void PlaceKeys()
    {
        var checkpoint = _graph.Start;
        StartHalf(chris: false);

        for (var guard = 0; guard < 10000; guard++)
        {
            var search = Search(checkpoint);

            // Pickups that keep their item hand it over when reached.
            foreach (var loc in search.Locations.Where(l => KeepsOriginal(l) && !_assigned.ContainsKey(l.GlobalId)))
            {
                Assign(loc, loc.OriginalType, IsKey(loc.OriginalType));
                if (IsKey(loc.OriginalType))
                    _have.Add(loc.OriginalType);
            }

            // Requirements the logic can do something about now: a missing key it may place.
            var missing = search.Missing
                .Where(r => r.Keys.Any(k => !_have.Contains(k)))
                .Where(r => r.Keys.Where(k => !_have.Contains(k)).All(k => Copies(k) > 0))
                .ToList();
            if (missing.Count == 0)
            {
                if (_visited.Contains(_graph.End))
                    return;

                // Nothing left to unlock here: go through a one-way door to the next area.
                var exit = search.Exits.FirstOrDefault()
                    ?? throw new ItemPlacementException($"The logic is stuck after {checkpoint}: the end ({_graph.End}) cannot be reached.");
                _log.Add($"--- through {exit} (one way) ---");
                checkpoint = exit.To;
                if (exit.To.IsChris && !exit.From.IsChris)
                {
                    if (!_settings.IncludeChris)
                        return;
                    StartHalf(chris: true);
                }
                continue;
            }

            // Place a key the area is waiting for: doors first, then the requirement with the fewest keys.
            var requirement = _rng.Shuffle(missing)
                .OrderBy(x => x.IsDoor ? 0 : 1)
                .ThenBy(x => x.Keys.Length)
                .First();
            var key = requirement.Keys.First(x => !_have.Contains(x));
            var copies = Copies(key);
            for (var i = 0; i < copies; i++)
            {
                var location = PickLocation(search.Locations)
                    ?? throw new ItemPlacementException(
                        $"No free pickup for {_items.NameOf(key)} near {checkpoint} (try another seed).");
                Assign(location, key, true);
                _keyOrder.Add(_assigned[location.GlobalId]);
                _log.Add($"{_items.NameOf(key)} -> {location}");
            }
            _have.Add(key);
        }
        throw new ItemPlacementException("Key item placement did not finish.");
    }

    private void StartHalf(bool chris)
    {
        _chrisHalf = chris;
        _have.Clear();
        foreach (var k in chris ? ChrisStartKeys : ClaireStartKeys)
            _have.Add(k);

        // Keys this half needs but has no pickup of: the game hands them over (events, the
        // other character) and the logic leaves them alone.
        var needed = RequiredKeys(_graph.Rooms.Values.Where(r => r.IsChris == chris));
        var available = _graph.Locations.Where(InHalf).Select(l => l.OriginalType).ToHashSet();
        foreach (var k in needed.Where(k => !available.Contains(k) && !_have.Contains(k)).Order())
        {
            _have.Add(k);
            _givenKeys.Add((chris ? "Chris" : "Claire", k));
        }
    }

    /// <summary>Pickups this half's player can take: Claire any she reaches, Chris only his own.</summary>
    private bool InHalf(ItemLocation l) => _chrisHalf ? l.ChrisOnly || (l.InChris && KeepsOriginal(l)) : l.InClaire;

    /// <summary>How many of a key to place: as many as the game has of it in this half.</summary>
    private int Copies(int type) =>
        _graph.Locations.Count(l => l.OriginalType == type && InHalf(l) && !KeepsOriginal(l));

    private ItemLocation? PickLocation(IEnumerable<ItemLocation> reachable)
    {
        var free = reachable.Where(l => !_assigned.ContainsKey(l.GlobalId) && !KeepsOriginal(l) && (!_chrisHalf || l.ChrisOnly)).ToList();
        var normal = free.Where(l => l.Priority == ItemPriority.Normal ||
                                     (_settings.HiddenKeyItems && l.Priority == ItemPriority.Hidden)).ToList();
        var pool = normal.Count != 0 ? normal : free;
        return pool.Count == 0 ? null : _rng.NextOf(pool);
    }

    private void Assign(ItemLocation location, int type, bool isKey) =>
        _assigned[location.GlobalId] = new ItemAssignment(location, type, isKey);

    private sealed record Requirement(int[] Keys, bool IsDoor);

    private sealed class SearchResult
    {
        public List<ItemLocation> Locations { get; } = [];
        public List<Requirement> Missing { get; } = [];
        public List<DoorEdge> Exits { get; } = [];
    }

    /// <summary>Everything reachable from <paramref name="start"/> with the keys in hand.</summary>
    private SearchResult Search(RoomNode start)
    {
        SearchResult result;
        int before;
        do
        {
            before = _visited.Count;
            result = new SearchResult();
            var locations = new HashSet<ItemLocation>();
            var seen = new HashSet<RoomNode>();
            var stack = new Stack<RoomNode>();
            stack.Push(start);
            while (stack.Count != 0)
            {
                var node = stack.Pop();
                if (!seen.Add(node))
                    continue;
                _visited.Add(node);

                if (node.Requires.Any(k => !_have.Contains(k)))
                    result.Missing.Add(new Requirement(node.Requires, false));

                foreach (var spot in node.Items)
                {
                    if (!spot.RequiresRoom.All(_visited.Contains))
                        continue;
                    if (spot.Requires.All(_have.Contains))
                    {
                        if (locations.Add(spot.Location))
                            result.Locations.Add(spot.Location);
                    }
                    else
                    {
                        result.Missing.Add(new Requirement(spot.Requires, false));
                    }
                }

                foreach (var edge in node.Edges)
                {
                    if (edge.Locked || !edge.RequiresRoom.All(_visited.Contains))
                        continue;
                    if (!edge.Requires.All(_have.Contains))
                    {
                        result.Missing.Add(new Requirement(edge.Requires, true));
                        continue;
                    }
                    if (edge.NoReturn)
                    {
                        if (!_visited.Contains(edge.To) && !result.Exits.Contains(edge))
                            result.Exits.Add(edge);
                        continue;
                    }
                    stack.Push(edge.To);
                }
            }
        } while (_visited.Count != before);   // a room requirement can open more on a second pass
        return result;
    }

    // ---------------------------------------------------------------- everything else

    private void FillOthers()
    {
        var free = _graph.Locations.Where(l => !_assigned.ContainsKey(l.GlobalId)).ToList();
        var keep = free.Where(KeepsOriginal).ToList();
        foreach (var l in keep)
            Assign(l, l.OriginalType, IsKey(l.OriginalType));
        free = free.Except(keep).ToList();

        if (_settings.ItemMode == ItemMode.Shuffle)
            FillShuffled(free);
        else
            FillCustom(free);
    }

    /// <summary>
    /// The game's own non-key items, moved around. Key items take the place of others, and keys
    /// a half does not need (Chris's copy of something only Claire uses...) become Green Herbs.
    /// </summary>
    private void FillShuffled(List<ItemLocation> free)
    {
        var pool = _graph.Locations
            .Where(l => !KeepsOriginal(l))
            .Select(l => l.OriginalType)
            .Where(t => !IsKey(t))
            .ToList();
        var shuffled = _rng.Shuffle(pool);
        foreach (var (location, i) in _rng.Shuffle(free).Select((l, i) => (l, i)))
            Assign(location, i < shuffled.Count ? shuffled[i] : 21, false);
    }

    /// <summary>New items: each allowed weapon once, then ammo for those weapons, healing, ink and gunpowder by ratio.</summary>
    private void FillCustom(List<ItemLocation> free)
    {
        var queue = new Queue<ItemLocation>(
            _rng.Shuffle(free.Where(l => l.Priority == ItemPriority.Normal))
                .Concat(_rng.Shuffle(free.Where(l => l.Priority != ItemPriority.Normal))));

        var weapons = _items.Items.Where(i => i.IsWeapon && i.WeaponKind != null && i.WeaponKind != "none")
            .Select(i => i.Id)
            .Where(id => _settings.Weapons.Length == 0 || _settings.Weapons.Contains(id))
            .Where(id => !_startWeapons.Contains(id))
            .ToList();
        var ammoTypes = _startWeapons.SelectMany(w => _items.Get(w)?.Ammo ?? []).ToList();
        foreach (var weapon in _rng.Shuffle(weapons))
        {
            if (queue.Count == 0)
                break;
            Assign(queue.Dequeue(), weapon, false);
            ammoTypes.AddRange(_items.Get(weapon)!.Ammo);
        }
        ammoTypes = ammoTypes.Distinct().Order().ToList();

        var tables = new List<(int Ratio, (int, double)[] Items)>
        {
            (_settings.RatioAmmo, ammoTypes.Select(t => (t, 1.0)).ToArray()),
            (_settings.RatioHealth, [(21, 0.5), (22, 0.15), (23, 0.25), (20, 0.1)]),   // herbs G/R/B, spray
            (_settings.RatioInkRibbons, [(31, 1.0)]),
            (_settings.RatioGunpowder, ammoTypes.Contains(18) ? [(35, 1.0)] : []),      // bow gun powder
        };
        var categories = tables.Where(t => t.Ratio > 0 && t.Items.Length != 0)
            .Select(t => (t.Items, (double)t.Ratio)).ToList();
        if (categories.Count == 0)
            throw new ItemPlacementException("No item ratios are set.");
        while (queue.Count != 0)
        {
            var category = _rng.NextWeighted(categories);
            Assign(queue.Dequeue(), _rng.NextWeighted(category), false);
        }
    }
}
