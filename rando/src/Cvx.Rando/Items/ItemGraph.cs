// The room graph for item logic, after BioRand's PlayGraph/DoorRandomiser.CreateOriginalGraph
// (MIT, Copyright (c) 2022-2026 Ted John).

using Cvx.Formats;
using Cvx.Rando.Data;
using Cvx.Rando.Doors;

namespace Cvx.Rando.Items;

public enum ItemPriority { Normal, Low, Fixed, Hidden }

/// <summary>
/// A pickup, identified by BioRand's global id. The same pickup can be in several room files:
/// variants of a room, and rooms that Claire and later Chris both visit (the game shares their
/// picked-up flags, so whoever comes first takes it).
/// </summary>
public sealed class ItemLocation
{
    public required int GlobalId { get; init; }
    public required int OriginalType { get; init; }
    public required ItemPriority Priority { get; init; }
    public List<RoomNode> Rooms { get; } = [];

    /// <summary>The first room it is listed in (for the spoiler log).</summary>
    public RoomNode Room => Rooms[0];
    public bool InClaire => Rooms.Any(r => !r.IsChris);
    public bool InChris => Rooms.Any(r => r.IsChris);
    public bool ChrisOnly => !InClaire;

    public override string ToString() => $"{Room.Id.BioRandId}#{GlobalId}";
}

/// <summary>A pickup as listed in one room, with that room's conditions.</summary>
public sealed record ItemSpot(ItemLocation Location, int[] Requires, RoomNode[] RequiresRoom);

public sealed class RoomNode
{
    public required RoomId Id { get; init; }
    public required string Key { get; init; }
    /// <summary>Chris's half (stage 9 rooms, BioRand <c>Axxx</c>).</summary>
    public bool IsChris => Id.Stage == 9;
    public int[] Requires { get; set; } = [];
    public List<DoorEdge> Edges { get; } = [];
    public List<ItemSpot> Items { get; } = [];

    public override string ToString() => Key;
}

public sealed class DoorEdge
{
    public required RoomNode From { get; init; }
    public required RoomNode To { get; init; }
    public int[] Requires { get; set; } = [];
    public RoomNode[] RequiresRoom { get; set; } = [];
    public bool NoReturn { get; init; }
    /// <summary>Random doors gave this door a new target (it is not paired with its original door).</summary>
    public bool Redirected { get; init; }
    /// <summary>Locked (always/side/gate): not passable for item logic.</summary>
    public bool Locked { get; init; }

    public override string ToString() => $"{From} -> {To}";
}

public sealed class ItemGraph
{
    public required RoomNode Start { get; init; }
    public required RoomNode End { get; init; }
    public required IReadOnlyDictionary<string, RoomNode> Rooms { get; init; }
    public required IReadOnlyList<ItemLocation> Locations { get; init; }

    /// <summary>
    /// Builds the original room graph. Pickups of documents are left out (never randomized), as
    /// are entries only meant for the other door mode.
    /// </summary>
    /// <param name="doors">Random doors: each door leads where the plan says, and the game starts in the plan's start room.</param>
    public static ItemGraph Build(RandoData data, RandoSettings settings, DoorPlan? doors = null)
    {
        var map = data.Map;
        var rooms = map.Rooms.Keys.ToDictionary(k => k, k => new RoomNode { Id = RoomId.ParseBioRand(k), Key = k }, StringComparer.Ordinal);
        RoomNode[] Nodes(string[]? keys) => (keys ?? []).Where(rooms.ContainsKey).Select(k => rooms[k]).ToArray();
        bool Applies(bool? doorRando) => doorRando == null || doorRando == settings.RandomDoors;

        var locations = new Dictionary<int, ItemLocation>();
        foreach (var (key, room) in map.Rooms)
        {
            var node = rooms[key];
            node.Requires = room.Requires ?? [];

            foreach (var (door, index) in (room.Doors ?? []).Select((d, i) => (d, i)))
            {
                if (!Applies(door.DoorRando) || door.Target == null || !rooms.ContainsKey(door.Target))
                    continue;
                var target = doors?.Find(key, index)?.TargetRoom ?? door.Target;
                if (target == key && doors?.Find(key, index) != null)
                    continue;   // a dead end: leads back into its own room
                node.Edges.Add(new DoorEdge
                {
                    From = node,
                    To = rooms[target],
                    Requires = door.Requires ?? [],
                    RequiresRoom = Nodes(door.RequiresRoom),
                    NoReturn = door.NoReturn,
                    Locked = DoorRandomizer.IsLocked(door),
                    Redirected = doors?.Find(key, index) != null,
                });
            }

            foreach (var item in room.Items ?? [])
            {
                if (!Applies(item.DoorRando) || item.Type is not int type || item.GlobalId is not int globalId)
                    continue;
                if (data.Items.Get(type)?.IsDocument == true)
                    continue;

                if (!locations.TryGetValue(globalId, out var location))
                {
                    locations[globalId] = location = new ItemLocation
                    {
                        GlobalId = globalId,
                        OriginalType = type,
                        Priority = ParsePriority(item.Priority),
                    };
                }
                else if (location.OriginalType != type)
                {
                    throw new InvalidDataException($"Global id {globalId} is {location.OriginalType} and {type} ({key}).");
                }
                if (!location.Rooms.Contains(node))
                {
                    location.Rooms.Add(node);
                    node.Items.Add(new ItemSpot(location, item.Requires ?? [], Nodes(item.RequiresRoom)));
                }
            }
        }

        SameKeysBothWays(rooms.Values);

        var beginEnd = map.BeginEndRooms.First(x => x.DoorRando == null || x.DoorRando == settings.RandomDoors);
        // With Chris, the logic runs to the last room of the game (BioRand's door-mode end,
        // A1E0); without, to Chris's start (BioRand's non-door end, A090).
        var end = map.BeginEndRooms.First(x => x.DoorRando == settings.IncludeChris).End!;

        return new ItemGraph
        {
            Start = rooms[doors?.StartRoom ?? beginEnd.Start!],
            End = rooms[end],
            Rooms = rooms,
            Locations = locations.Values.OrderBy(x => x.GlobalId).ToList(),
        };
    }

    /// <summary>
    /// A door that keeps its original pairing needs its keys from both sides. The room data
    /// often lists a key on one side only, the side the normal game reaches it from: RM_0080's
    /// Eagle Plate door also locks the fenced corner RM_0160 leads into, which random doors can
    /// reach first. Requiring the keys from the other side too can only make the logic stricter.
    /// </summary>
    private static void SameKeysBothWays(IEnumerable<RoomNode> rooms)
    {
        var all = rooms.SelectMany(r => r.Edges).Where(e => !e.Redirected).ToList();
        foreach (var edge in all.Where(e => e.Requires.Length != 0))
        {
            foreach (var back in all.Where(b => b.From == edge.To && b.To == edge.From))
            {
                if (!edge.Requires.All(back.Requires.Contains))
                    back.Requires = back.Requires.Union(edge.Requires).Order().ToArray();
            }
        }
    }

    private static ItemPriority ParsePriority(string? s) =>
        s == null ? ItemPriority.Normal : Enum.Parse<ItemPriority>(s, ignoreCase: true);
}
