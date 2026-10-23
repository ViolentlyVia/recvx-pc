namespace Cvx.Rando.Items;

/// <summary>
/// Plays a finished item placement through on the room graph, independently of
/// <see cref="ItemPlacer"/>: collects every pickup it can reach, opens what the keys open, takes
/// one-way doors only once nothing else is left, and switches inventory at Chris's handover
/// (not with random doors: one character plays the whole run).
/// Claire takes everything she can, so pickups she shares with Chris are gone for him (the
/// worst case), except shared pickups that keep their original key, which behave as in the
/// normal game.
/// </summary>
public static class Playthrough
{
    public sealed record Result(bool Completed, string Message, int Pickups);

    public static Result Check(ItemGraph graph, ItemPlacementResult placement)
    {
        var current = graph.Start;
        var have = graph.OnlyCharacter is { } only ? StartKeys(only == Character.Chris, placement) : StartKeys(current.IsChris, placement);
        var collected = new HashSet<int>();
        var visited = new HashSet<RoomNode>();

        for (var area = 0; area < 100; area++)
        {
            var exits = new List<DoorEdge>();
            bool progress;
            do
            {
                progress = false;
                exits.Clear();
                var seen = new HashSet<RoomNode>();
                var stack = new Stack<RoomNode>();
                stack.Push(current);
                while (stack.Count != 0)
                {
                    var node = stack.Pop();
                    if (!seen.Add(node))
                        continue;
                    if (visited.Add(node))
                        progress = true;
                    foreach (var spot in node.Items)
                    {
                        var g = spot.Location.GlobalId;
                        if (collected.Contains(g) || !spot.Requires.All(have.Contains) || !spot.RequiresRoom.All(visited.Contains))
                            continue;
                        collected.Add(g);
                        if (placement.ByGlobalId.TryGetValue(g, out var a))
                            have.Add(a.Type);
                        progress = true;
                    }
                    foreach (var edge in node.Edges)
                    {
                        if (edge.Locked || !edge.Requires.All(have.Contains) || !edge.RequiresRoom.All(visited.Contains))
                            continue;
                        if (edge.NoReturn)
                        {
                            if (!visited.Contains(edge.To))
                                exits.Add(edge);
                        }
                        else
                        {
                            stack.Push(edge.To);
                        }
                    }
                }
            } while (progress);

            if (visited.Contains(graph.End))
                return new Result(true, "end reached", collected.Count);
            if (exits.Count == 0)
                return new Result(false, $"stuck around {current} with {collected.Count} pickups", collected.Count);

            var exit = exits[0];
            current = exit.To;
            if (graph.OnlyCharacter == null && exit.To.IsChris && !exit.From.IsChris)
            {
                have = StartKeys(true, placement);
                collected.RemoveWhere(g => placement.ByGlobalId.TryGetValue(g, out var a) &&
                    a.IsKey && a.Type == a.Location.OriginalType && a.Location.InChris && a.Location.InClaire);
            }
        }
        return new Result(false, "too many areas", collected.Count);
    }

    /// <summary>
    /// A room the player can walk into and not back out of with the keys they have, or null.
    /// With the keys of each step of the key order, every room reached from a start (the run's,
    /// or the far side of a one-way door) must lead back to it: the item logic counts on going
    /// back. A random link keyed on one side is one-way from the other (seed 985463 came into
    /// RM_9320 by the open side of its jewel door; seed 7 into RM_1070 from RM_3160, reached
    /// another way). The closed side of a one-side door opens once its open side was reached.
    /// Doors the game itself makes one-way (story doors, doors locked for good on the far side)
    /// are not walked through; their far sides are starts of their own.
    /// </summary>
    public static string? FindTrap(ItemGraph graph, ItemPlacementResult placement)
    {
        var starts = new List<RoomNode> { graph.Start };
        starts.AddRange(graph.Rooms.Values.SelectMany(r => r.Edges).Where(OneWay).Select(e => e.To).Distinct().Where(r => r != graph.Start));
        var start = graph.OnlyCharacter is { } only ? StartKeys(only == Character.Chris, placement) : StartKeys(graph.Start.IsChris, placement);
        var order = placement.KeyOrder.Select(k => k.Type).ToList();
        for (var step = 0; step <= order.Count; step++)
        {
            var have = new HashSet<int>(start.Concat(order.Take(step)));
            foreach (var from in starts)
            {
                var reached = Reach(from, have, null);
                foreach (var room in reached)
                {
                    if (!Reach(room, have, reached).Contains(from))
                        return $"{room} is reached from {from} with {step} keys of the key order but does not lead back";
                }
            }
        }
        return null;
    }

    /// <summary>A door with no way back through it (keys aside): a story door, or one whose far side is locked for good.</summary>
    private static bool OneWay(DoorEdge e) =>
        e.NoReturn || (!e.Unblock && !e.To.Edges.Any(b => b.To == e.From && !b.Locked && !b.NoReturn));

    private static HashSet<RoomNode> Reach(RoomNode from, HashSet<int> have, HashSet<RoomNode>? visited)
    {
        var seen = new HashSet<RoomNode>();
        var stack = new Stack<RoomNode>([from]);
        bool Opened(DoorEdge e) => visited != null && visited.Contains(e.To) &&
            e.To.Edges.Any(b => b.To == e.From && b.Unblock && b.Requires.All(have.Contains));
        while (stack.Count != 0)
        {
            var r = stack.Pop();
            if (!seen.Add(r))
                continue;
            foreach (var e in r.Edges.Where(e => (!e.Locked || Opened(e)) && !OneWay(e) && e.Requires.All(have.Contains) &&
                                                 e.RequiresRoom.All(x => (visited ?? seen).Contains(x))))
                stack.Push(e.To);
        }
        return seen;
    }

    private static HashSet<int> StartKeys(bool chris, ItemPlacementResult placement)
    {
        var have = new HashSet<int>(chris ? ItemPlacer.ChrisStartKeys : ItemPlacer.ClaireStartKeys);
        foreach (var (half, type) in placement.GivenKeys)
        {
            if ((half == "Chris") == chris)
                have.Add(type);
        }
        return have;
    }
}
