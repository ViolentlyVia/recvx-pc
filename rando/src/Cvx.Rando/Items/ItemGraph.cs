// The room graph for item logic, after BioRand's PlayGraph/DoorRandomiser.CreateOriginalGraph
// (MIT, Copyright (c) 2022-2026 Ted John).

using Cvx.Formats;
using Cvx.Rando.Data;

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
    public int[] Requires { get; init; } = [];
    public RoomNode[] RequiresRoom { get; set; } = [];
    public bool NoReturn { get; init; }
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
    public static ItemGraph Build(RandoData data, RandoSettings settings)
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

            foreach (var door in room.Doors ?? [])
            {
                if (!Applies(door.DoorRando) || door.Target == null || !rooms.ContainsKey(door.Target))
                    continue;
                node.Edges.Add(new DoorEdge
                {
                    From = node,
                    To = rooms[door.Target],
                    Requires = door.Requires ?? [],
                    RequiresRoom = Nodes(door.RequiresRoom),
                    NoReturn = door.NoReturn,
                    Locked = door.Lock is { } l && !l.Equals("none", StringComparison.OrdinalIgnoreCase)
                                                && !l.Equals("unblock", StringComparison.OrdinalIgnoreCase),
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

        var beginEnd = map.BeginEndRooms.First(x => x.DoorRando == null || x.DoorRando == settings.RandomDoors);
        // With Chris, the logic runs to the last room of the game (BioRand's door-mode end,
        // A1E0); BioRand's non-door data stops at Chris's start (A090).
        var end = settings.IncludeChris
            ? map.BeginEndRooms.First(x => x.DoorRando == true).End!
            : beginEnd.End!;

        return new ItemGraph
        {
            Start = rooms[beginEnd.Start!],
            End = rooms[end],
            Rooms = rooms,
            Locations = locations.Values.OrderBy(x => x.GlobalId).ToList(),
        };
    }

    private static ItemPriority ParsePriority(string? s) =>
        s == null ? ItemPriority.Normal : Enum.Parse<ItemPriority>(s, ignoreCase: true);
}
