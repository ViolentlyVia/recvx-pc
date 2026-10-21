using System.Text;
using Cvx.Formats;
using Cvx.Rando.Data;

namespace Cvx.Rando.Doors;

/// <summary>Writes rando_doors.txt for recvx-pc (pc_rando.c, read_doors).</summary>
public static class DoorTable
{
    public const string FileName = "rando_doors.txt";

    public static string Write(DoorPlan plan, RandoData data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Written by cvx-rando; read by recvx.exe (pc_rando.c).");
        sb.AppendLine("# stage room case area old_stage old_room  new_stage new_room new_case entrance [1: locked, leads nowhere]");
        var mapped = data.Map.Rooms.Keys.Select(RoomId.ParseBioRand).ToHashSet();
        foreach (var link in plan.Links.Values.OrderBy(l => l.Door.Room, StringComparer.Ordinal).ThenBy(l => l.Door.Index))
        {
            var room = RoomId.ParseBioRand(link.Door.Room);
            var oldTarget = RoomId.ParseBioRand(link.Door.OriginalTarget);
            var to = RoomId.ParseBioRand(link.TargetRoom);

            // The room file the data describes, and its variants the data does not list.
            var cases = new List<int> { room.Variant };
            for (var c = 0; c <= 2; c++)
            {
                var variant = room with { Variant = c };
                if (c != room.Variant && RoomTable.Contains(variant) && !mapped.Contains(variant))
                    cases.Add(c);
            }
            foreach (var c in cases)
            {
                sb.AppendLine($"{room.Stage} {room.Room} {c} {link.Door.Area} {oldTarget.Stage} {oldTarget.Room}" +
                              $"  {to.Stage} {to.Room} {to.Variant} {link.Arrival}{(link.Locked ? " 1" : "")}");
            }
        }
        return sb.ToString();
    }
}
