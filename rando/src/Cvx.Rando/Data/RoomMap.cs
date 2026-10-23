// The room graph model, ported from BioRand's Map.cs (MIT, Copyright (c) 2022-2026 Ted John) and
// trimmed to what its Code: Veronica data (data/recv/rdt.json) uses.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cvx.Formats;

namespace Cvx.Rando.Data;

public sealed class RoomMap
{
    public MapStartEnd[] BeginEndRooms { get; set; } = [];

    /// <summary>Rooms by BioRand id (<c>10A0</c>); see <see cref="RoomId.ParseBioRand"/>.</summary>
    public Dictionary<string, MapRoom> Rooms { get; set; } = [];

    public MapRoom? GetRoom(RoomId id) => Rooms.TryGetValue(id.BioRandId, out var room) ? room : null;
}

public sealed class MapStartEnd
{
    public string? Start { get; set; }
    public string? End { get; set; }
    public bool? DoorRando { get; set; }
}

public sealed class MapRoom
{
    public int[]? Requires { get; set; }
    public string[]? RequiresRoom { get; set; }
    public MapRoomDoor[]? Doors { get; set; }
    public MapRoomItem[]? Items { get; set; }
    public MapRoomEnemies[]? Enemies { get; set; }
    public DoorRandoSpec[]? DoorRando { get; set; }
}

public sealed class MapRoomDoor
{
    /// <summary>Trigger area (<c>etcp</c>) index; 255 for a door made by the script.</summary>
    public int? Id { get; set; }
    public int? AltId { get; set; }
    /// <summary>Script opcodes that hold this door's target instead of the trigger area.</summary>
    public OffsetSpec[]? Offsets { get; set; }
    public string? Target { get; set; }
    public int? EntranceId { get; set; }
    public MapRoomDoorEntrance? Entrance { get; set; }
    public string? Condition { get; set; }
    public bool? Randomize { get; set; }
    public string? Lock { get; set; }
    public byte? LockId { get; set; }
    public bool NoReturn { get; set; }
    public bool NoUnlock { get; set; }
    public bool IsBridgeEdge { get; set; }
    public int[]? Requires { get; set; }
    public string[]? RequiresRoom { get; set; }
    public bool? DoorRando { get; set; }
    public bool Create { get; set; }
    public int Texture { get; set; }
    public int? Special { get; set; }
    public byte? Cut { get; set; }
}

public sealed class MapRoomDoorEntrance
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int D { get; set; }
    public int Floor { get; set; }
    public int Cut { get; set; }
}

public sealed class MapRoomItem
{
    /// <summary>Trigger area index; 254/255 for items the script gives (no pickup in the room).</summary>
    public int Id { get; set; }
    /// <summary>Index in the room's item table when it is not the trigger area's <c>prm0</c>.</summary>
    public int? ItemId { get; set; }
    public int? GlobalId { get; set; }
    public int? Type { get; set; }
    public int? Amount { get; set; }
    /// <summary>Script opcodes that repeat this item's type.</summary>
    public OffsetSpec[]? Offsets { get; set; }
    public OffsetSpec[]? Nop { get; set; }
    public string? Priority { get; set; }
    public int[]? Requires { get; set; }
    public string[]? RequiresRoom { get; set; }
    public bool? DoorRando { get; set; }
}

public sealed class MapRoomEnemies
{
    public OffsetSpec[]? Nop { get; set; }
    public int[]? ExcludeTypes { get; set; }
    public int[]? IncludeTypes { get; set; }
    public int? MaxDifficulty { get; set; }
    public bool? Restricted { get; set; }
    public bool KeepState { get; set; }
    public bool KeepAi { get; set; }
    public bool KeepPositions { get; set; }
    public bool? DoorRando { get; set; }
}

public sealed class DoorRandoSpec
{
    public string? Category { get; set; }
    public OffsetSpec[]? Nop { get; set; }
}

/// <summary>
/// An offset or an inclusive range of offsets into the expanded room file: <c>1625480</c>,
/// <c>"0x18C56A"</c> or <c>"0x18C56A-0x18C57A"</c>.
/// </summary>
[JsonConverter(typeof(OffsetSpecConverter))]
public readonly record struct OffsetSpec(int Start, int End)
{
    public bool IsRange => End != Start;
    public bool Contains(int offset) => offset >= Start && offset <= End;
    public override string ToString() => IsRange ? $"0x{Start:X}-0x{End:X}" : $"0x{Start:X}";

    public static OffsetSpec Parse(string text)
    {
        var dash = text.IndexOf('-', 1);
        if (dash > 0)
            return new OffsetSpec(ParseNumber(text[..dash]), ParseNumber(text[(dash + 1)..]));
        var n = ParseNumber(text);
        return new OffsetSpec(n, n);
    }

    private static int ParseNumber(string s)
    {
        s = s.Trim();
        return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.Parse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : int.Parse(s, CultureInfo.InvariantCulture);
    }
}

internal sealed class OffsetSpecConverter : JsonConverter<OffsetSpec>
{
    public override OffsetSpec Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Number => new OffsetSpec(reader.GetInt32(), reader.GetInt32()),
            JsonTokenType.String => OffsetSpec.Parse(reader.GetString()!),
            _ => throw new JsonException($"Expected an offset, got {reader.TokenType}."),
        };

    public override void Write(Utf8JsonWriter writer, OffsetSpec value, JsonSerializerOptions options)
    {
        if (value.IsRange)
            writer.WriteStringValue(value.ToString());
        else
            writer.WriteNumberValue(value.Start);
    }
}
