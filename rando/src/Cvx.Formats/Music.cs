using System.Buffers.Binary;
using Cvx.Formats.Rdx;
using Cvx.Formats.Script;

namespace Cvx.Formats;

/// <summary>What an ADX file's header says (CRI ADX, big-endian).</summary>
public readonly record struct AdxInfo(int Channels, int SampleRate, int Samples, bool Loops)
{
    public double Seconds => SampleRate == 0 ? 0 : (double)Samples / SampleRate;

    public static AdxInfo? Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 0x30 || data[0] != 0x80 || data[1] != 0x00)
            return null;
        var channels = data[7];
        var rate = (int)BinaryPrimitives.ReadUInt32BigEndian(data[8..]);
        var samples = (int)BinaryPrimitives.ReadUInt32BigEndian(data[0xC..]);
        var version = data[0x12];
        // the loop flag: version 3 at 0x18, version 4 at 0x24
        var loopAt = version == 4 ? 0x24 : 0x18;
        var loops = data.Length >= loopAt + 4 && BinaryPrimitives.ReadUInt32BigEndian(data[loopAt..]) != 0;
        return new AdxInfo(channels, rate, samples, loops);
    }
}

/// <summary>A script's request for a music track.</summary>
public readonly record struct MusicRequest(RoomId Room, int Offset, string Opcode, int Track);

/// <summary>
/// The music: BGM1.AFS (disc 1) and BGM2.AFS (disc 2). A track number is an entry index in the
/// current disc's archive (sdfunc.c <c>PlayBgmEx</c>); each disc only holds the tracks it uses,
/// the other entries are 72-byte placeholders.
/// </summary>
public static class Music
{
    public const int PlaceholderSize = 72;

    /// <summary>Music opcodes and where their track number is (event.c bhBgmOn..., bhSetNextRoomBgm).</summary>
    public static readonly IReadOnlyDictionary<string, int> TrackByte = new Dictionary<string, int>
    {
        ["bgm_on"] = 1,
        ["bgm_on_2"] = 1,
        ["bgm_on_ex"] = 1,
        ["bgm_on_2_ex"] = 1,
        ["set_next_room_bgm"] = 6,
    };

    /// <summary>Each disc's tracks: number -> header (missing for placeholders).</summary>
    public static Dictionary<int, AdxInfo> ReadDisc(GameData data, int disc)
    {
        using var afs = AfsArchive.Open(Path.Combine(data.Directory, $"BGM{disc}.AFS"));
        var result = new Dictionary<int, AdxInfo>();
        for (var i = 0; i < afs.Count; i++)
        {
            if (afs.Entries[i].Size <= PlaceholderSize)
                continue;
            var head = afs.ReadEntry(i).AsSpan(0, Math.Min(64, afs.Entries[i].Size));
            if (AdxInfo.Read(head) is { } info)
                result[i] = info;
        }
        return result;
    }

    public static IEnumerable<MusicRequest> Requests(RoomId room, RdxFile file)
    {
        foreach (var op in CvScriptReader.ReadOpcodes(file))
        {
            if (TrackByte.TryGetValue(op.Name, out var at) && at < op.Bytes.Length)
                yield return new MusicRequest(room, op.Offset, op.Name, op.Bytes.Span[at]);
        }
    }
}
