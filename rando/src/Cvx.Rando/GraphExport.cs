using System.Text;
using System.Xml.Linq;
using Cvx.Rando.Data;
using Cvx.Rando.Items;

namespace Cvx.Rando;

/// <summary>
/// The room graph of a seed for viewing: graph.dot (Graphviz) and graph.dgml (Visual Studio).
/// Two-way doors are drawn once without arrows; keys a door needs are its label; one-way doors
/// are dashed.
/// </summary>
public static class GraphExport
{
    private sealed record Line(RoomNode A, RoomNode B, bool TwoWay, bool NoReturn, string Label);

    private static List<Line> Lines(ItemGraph graph, ItemCatalog items)
    {
        string Keys(DoorEdge e) => string.Join(", ", e.Requires.Select(items.NameOf));
        var result = new List<Line>();
        var done = new HashSet<DoorEdge>();
        foreach (var node in graph.Rooms.Values.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            foreach (var edge in node.Edges.Where(e => !e.Locked))
            {
                if (!done.Add(edge))
                    continue;
                var back = edge.NoReturn ? null : edge.To.Edges.FirstOrDefault(e => e.To == node && !e.NoReturn && !e.Locked && !done.Contains(e));
                if (back != null)
                    done.Add(back);
                var label = string.Join(" / ", new[] { Keys(edge), back == null ? "" : Keys(back) }.Where(x => x.Length != 0).Distinct());
                result.Add(new Line(node, edge.To, back != null, edge.NoReturn, label));
            }
        }
        return result;
    }

    private static IEnumerable<RoomNode> Reachable(ItemGraph graph)
    {
        var seen = new HashSet<RoomNode>();
        var stack = new Stack<RoomNode>([graph.Start]);
        while (stack.Count != 0)
        {
            var r = stack.Pop();
            if (!seen.Add(r))
                continue;
            foreach (var e in r.Edges.Where(e => !e.Locked))
                stack.Push(e.To);
        }
        return seen.OrderBy(r => r.Key, StringComparer.Ordinal);
    }

    private static string Label(RoomNode r) => $"{r.Id.Name} ({r.Key})";

    public static string Dot(ItemGraph graph, ItemCatalog items, ISet<string>? saveRooms = null)
    {
        var rooms = Reachable(graph).ToHashSet();
        var sb = new StringBuilder();
        sb.AppendLine("digraph cvx {");
        sb.AppendLine("  node [shape=box, fontname=\"Segoe UI\", fontsize=10];");
        sb.AppendLine("  edge [fontname=\"Segoe UI\", fontsize=9];");
        foreach (var r in rooms.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            var style = r == graph.Start ? ", style=filled, fillcolor=\"#b8e0b8\"" :
                        r == graph.End ? ", style=filled, fillcolor=\"#e0b8b8\"" :
                        saveRooms?.Contains(r.Key) == true ? ", style=filled, fillcolor=\"#b8cce0\"" : "";
            sb.AppendLine($"  \"{r.Key}\" [label=\"{Label(r)}\"{style}];");
        }
        foreach (var l in Lines(graph, items).Where(l => rooms.Contains(l.A)))
        {
            var attrs = new List<string>();
            if (l.TwoWay)
                attrs.Add("dir=none");
            if (l.NoReturn)
                attrs.Add("style=dashed");
            if (l.Label.Length != 0)
                attrs.Add($"label=\"{l.Label}\"");
            sb.AppendLine($"  \"{l.A.Key}\" -> \"{l.B.Key}\"{(attrs.Count != 0 ? $" [{string.Join(", ", attrs)}]" : "")};");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    public static string Dgml(ItemGraph graph, ItemCatalog items, ISet<string>? saveRooms = null)
    {
        XNamespace ns = "http://schemas.microsoft.com/vs/2009/dgml";
        var rooms = Reachable(graph).ToHashSet();
        string Category(RoomNode r) => r == graph.Start ? "Start" : r == graph.End ? "End" : saveRooms?.Contains(r.Key) == true ? "Save" : "Room";
        var doc = new XDocument(
            new XElement(ns + "DirectedGraph",
                new XElement(ns + "Nodes", rooms.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r =>
                    new XElement(ns + "Node", new XAttribute("Id", r.Key), new XAttribute("Label", Label(r)),
                        new XAttribute("Category", Category(r))))),
                new XElement(ns + "Links", Lines(graph, items).Where(l => rooms.Contains(l.A)).Select(l =>
                {
                    var e = new XElement(ns + "Link", new XAttribute("Source", l.A.Key), new XAttribute("Target", l.B.Key),
                        new XAttribute("Category", l.NoReturn ? "OneWay" : l.TwoWay ? "TwoWay" : "Door"));
                    if (l.Label.Length != 0)
                        e.Add(new XAttribute("Label", l.Label));
                    return e;
                })),
                new XElement(ns + "Categories",
                    new XElement(ns + "Category", new XAttribute("Id", "Start"), new XAttribute("Background", "#FFB8E0B8")),
                    new XElement(ns + "Category", new XAttribute("Id", "End"), new XAttribute("Background", "#FFE0B8B8")),
                    new XElement(ns + "Category", new XAttribute("Id", "Save"), new XAttribute("Background", "#FFB8CCE0")),
                    new XElement(ns + "Category", new XAttribute("Id", "Room")),
                    new XElement(ns + "Category", new XAttribute("Id", "Door")),
                    new XElement(ns + "Category", new XAttribute("Id", "TwoWay")),
                    new XElement(ns + "Category", new XAttribute("Id", "OneWay"), new XAttribute("StrokeDashArray", "4 2")))));
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine + doc;
    }
}
