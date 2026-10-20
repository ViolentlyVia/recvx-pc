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
