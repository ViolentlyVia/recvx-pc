using System.Text;
using Cvx.Rando.Data;

namespace Cvx.Rando;

/// <summary>The spoiler log: where everything went.</summary>
public static class Spoiler
{
    public static string Write(GenerationResult result, RandoData data)
    {
        var s = result.Settings;
        var names = data.Items;
        var sb = new StringBuilder();
        sb.AppendLine("CVX Randomizer spoiler log");
        sb.AppendLine("==========================");
        sb.AppendLine($"Seed: {s.Seed}");
        sb.AppendLine($"Items: {(s.RandomItems ? s.ItemMode.ToString().ToLowerInvariant() : "off")}" +
                      $"{(s.RandomItems && !s.IncludeChris ? " (Claire's half only)" : "")}");
        sb.AppendLine();

        if (result.Items is { } items)
        {
            sb.AppendLine("Key items, in the order the logic expects them");
            sb.AppendLine("----------------------------------------------");
            foreach (var a in items.KeyOrder)
            {
                var half = a.Location.Room.IsChris ? "Chris" : "Claire";
                sb.AppendLine($"  {names.NameOf(a.Type),-28} {RoomLabel(a.Location.Room.Id)}  ({half})");
            }
            if (items.GivenKeys.Count != 0)
            {
                sb.AppendLine();
                sb.AppendLine("  Handed over by the game (not randomized):");
                foreach (var (half, type) in items.GivenKeys)
                    sb.AppendLine($"    {names.NameOf(type)} ({half})");
            }
            sb.AppendLine();

            sb.AppendLine("All pickups by room");
            sb.AppendLine("-------------------");
            foreach (var room in items.ByGlobalId.Values.GroupBy(a => a.Location.Room).OrderBy(g => g.Key.Key, StringComparer.Ordinal))
            {
                sb.AppendLine($"  {RoomLabel(room.Key.Id)}");
                foreach (var a in room.OrderBy(a => a.Location.GlobalId))
                {
                    var was = a.Location.OriginalType == a.Type ? "(unchanged)" : $"(was {names.NameOf(a.Location.OriginalType)})";
                    sb.AppendLine($"    {names.NameOf(a.Type),-28} {was}{(a.IsKey ? "  [key]" : "")}");
                }
            }
            sb.AppendLine();
        }

        if (result.PickupAmounts.Count != 0)
        {
            sb.AppendLine("Pickup amounts");
            sb.AppendLine("--------------");
            foreach (var (type, amount) in result.PickupAmounts.OrderBy(x => x.Key))
                sb.AppendLine($"  {names.NameOf(type),-28} {amount}");
            sb.AppendLine();
        }

        sb.AppendLine($"{result.ChangedRooms.Count} room files changed, {result.Patches.Count} patches.");
        return sb.ToString();
    }

    private static string RoomLabel(Cvx.Formats.RoomId id) => $"{id.Name} ({id.BioRandId})";
}
