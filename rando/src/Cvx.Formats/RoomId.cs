using System.Globalization;

namespace Cvx.Formats;

/// <summary>
/// A room, as the game names its files: <c>RM_&lt;stage&gt;&lt;room, 2 decimal digits&gt;&lt;variant&gt;.RDX</c>.
/// </summary>
/// <remarks>
/// BioRand writes the same room as four hex digits <c>SRRV</c> with the stage one higher:
/// <c>RM_0100</c> is <c>10A0</c>, <c>RM_9321</c> is <c>A201</c>.
/// </remarks>
public readonly record struct RoomId(int Stage, int Room, int Variant) : IComparable<RoomId>
{
    /// <summary><c>RM_0100</c></summary>
    public string Name => $"RM_{Stage}{Room:00}{Variant}";
    public string FileName => Name + ".RDX";

    /// <summary>BioRand's id, e.g. <c>10A0</c>.</summary>
    public string BioRandId => $"{Stage + 1:X1}{Room:X2}{Variant:X1}";

    public override string ToString() => Name;

    public int CompareTo(RoomId other) =>
        (Stage, Room, Variant).CompareTo((other.Stage, other.Room, other.Variant));

    /// <summary>Accepts <c>RM_0100</c>, <c>RM_0100.RDX</c> or <c>0100</c>.</summary>
    public static bool TryParseName(string text, out RoomId id)
    {
        id = default;
        var s = text.Trim().ToUpperInvariant();
        if (s.EndsWith(".RDX", StringComparison.Ordinal))
            s = s[..^4];
        if (s.StartsWith("RM_", StringComparison.Ordinal))
            s = s[3..];
        if (s.Length != 4 || !s.All(char.IsAsciiDigit))
            return false;
        id = new RoomId(s[0] - '0', int.Parse(s.AsSpan(1, 2), CultureInfo.InvariantCulture), s[3] - '0');
        return true;
    }

    public static RoomId ParseName(string text) =>
        TryParseName(text, out var id) ? id : throw new FormatException($"Not a room name: {text}");

    /// <summary>Parses BioRand's <c>SRRV</c> form (hex).</summary>
    public static bool TryParseBioRand(string text, out RoomId id)
    {
        id = default;
        var s = text.Trim();
        if (s.Length is < 3 or > 4)
            return false;
        if (!int.TryParse(s.AsSpan(0, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var stage) ||
            !int.TryParse(s.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var room))
        {
            return false;
        }
        var variant = 0;
        if (s.Length == 4 && !int.TryParse(s.AsSpan(3, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out variant))
            return false;
        if (stage < 1)
            return false;
        id = new RoomId(stage - 1, room, variant);
        return true;
    }

    public static RoomId ParseBioRand(string text) =>
        TryParseBioRand(text, out var id) ? id : throw new FormatException($"Not a BioRand room id: {text}");
}

/// <summary>
/// The 205 rooms in RDX_LNK.AFS, in archive order (<c>rdx_files</c> in the game's ps2_dvd_image.c).
/// </summary>
public static class RoomTable
{
    private static readonly string[] Names =
    [
        "0000", "0010", "0020", "0021", "0030", "0031", "0040", "0050", "0060", "0070",
        "0080", "0090", "0100", "0110", "0120", "0130", "0140", "0150", "0160",
        "1000", "1001", "1002", "1020", "1021", "1030", "1040", "1050", "1060", "1070",
        "1080", "1090", "1100", "1110", "1120", "1121", "1122", "1130", "1140",
        "2000", "2010", "2011", "2020", "2030", "2031", "2040", "2050", "2060", "2070",
        "3000", "3010", "3011", "3020", "3030", "3040", "3050", "3060", "3070", "3080",
        "3090", "3091", "3100", "3110", "3120", "3130", "3140", "3150", "3160", "3170",
        "3180", "3190", "3200", "3210", "3220", "3230", "3240",
        "4000", "4001", "4010", "4011", "4012", "4020", "4030", "4040", "4050",
        "5000", "5001", "5010", "5500", "5510", "5520", "5530", "5540", "5550", "5580",
        "5590", "5600", "5610", "5620", "5630", "5640", "5650", "5660", "5670", "5680",
        "5690", "5700", "5710", "5800", "5810", "5820", "5830", "5840",
        "6000", "6010", "6020", "6030", "6040", "6050", "6051", "6060", "6070", "6080",
        "6090", "6100",
        "7000", "7010", "7020", "7030", "7031", "7040", "7050", "7060", "7070", "7071",
        "7080", "7081", "7090", "7100", "7110", "7120", "7130", "7140", "7150", "7160",
        "7170", "7180", "7181", "7190", "7191", "7200", "7210", "7220", "7221", "7230",
        "7231", "7240", "7250",
        "8000", "8001", "8010", "8020", "8030", "8040", "8050",
        "9000", "9010", "9020", "9030", "9040", "9050", "9060", "9070", "9080", "9090",
        "9091", "9100", "9110", "9120", "9130", "9140", "9150", "9160", "9170", "9180",
        "9190", "9200", "9210", "9220", "9230", "9240", "9250", "9260", "9270", "9280",
        "9290", "9300", "9301", "9302", "9310", "9320", "9321", "9340", "9350", "9360",
        "9370",
    ];

    private static readonly RoomId[] Ids = Names.Select(RoomId.ParseName).ToArray();
    private static readonly Dictionary<RoomId, int> Indices =
        Ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);

    public const int Count = 205;

    public static IReadOnlyList<RoomId> All => Ids;

    public static RoomId Get(int index) => Ids[index];

    public static int IndexOf(RoomId id) => Indices.TryGetValue(id, out var i) ? i : -1;

    public static bool Contains(RoomId id) => Indices.ContainsKey(id);
}
