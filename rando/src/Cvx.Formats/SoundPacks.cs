using System.Buffers.Binary;

namespace Cvx.Formats;

/// <summary>One bank of a sound pack (<c>SPQ_HEADER</c>, sdfunc.h).</summary>
/// <param name="Type">0 MIDI sequences, 1 MIDI programs, 2 shots, 3 effect programs, 4 effect output, 5 room sound settings.</param>
public sealed record SoundBank(int Type, int BankNo, ReadOnlyMemory<byte> Data);

/// <summary>
/// The room sound packs (<c>RM_SRRC.SPQ</c>) in MULTSPQ1.AFS and MULTSPQ2.AFS (disc 1 and 2;
/// sdfunc.c <c>LoadSoundPackFile</c>). MULTSPQn.IDX lists each entry's key, 16 bits:
/// stage * 1000 + room * 10 + case for rooms; other packs set high bits.
/// A pack is a list of 16-byte headers (offset, size, type, bank) ended by offset 0.
/// </summary>
public sealed class SoundPacks : IDisposable
{
    private readonly AfsArchive _afs;
    private readonly ushort[] _keys;

    public string Name { get; }

    private SoundPacks(string name, AfsArchive afs, ushort[] keys)
    {
        Name = name;
        _afs = afs;
        _keys = keys;
    }

    /// <param name="disc">1 or 2.</param>
    public static SoundPacks Open(GameData data, int disc)
    {
        var name = $"MULTSPQ{disc}";
        var idx = File.ReadAllBytes(Path.Combine(data.Directory, name + ".IDX"));
        var keys = new List<ushort>();
        for (var i = 0; i + 2 <= idx.Length; i += 2)
        {
            var k = BinaryPrimitives.ReadUInt16LittleEndian(idx.AsSpan(i));
            if (k == 0xFFFF)
                break;
            keys.Add(k);
        }
        return new SoundPacks(name, AfsArchive.Open(Path.Combine(data.Directory, name + ".AFS")), keys.ToArray());
    }

    public AfsArchive Afs => _afs;
    public IReadOnlyList<ushort> Keys => _keys;

    public static ushort RoomKey(RoomId id) => (ushort)(id.Stage * 1000 + id.Room * 10 + id.Variant);

    /// <summary>The AFS entry of a room's pack, or -1.</summary>
    public int IndexOf(RoomId id) => Array.IndexOf(_keys, RoomKey(id));

    public byte[]? ReadRoom(RoomId id) => IndexOf(id) is var i and >= 0 ? _afs.ReadEntry(i) : null;

    public static List<SoundBank> Parse(ReadOnlySpan<byte> pack)
    {
        var result = new List<SoundBank>();
        var copy = pack.ToArray();
        for (var p = 0; p + 16 <= pack.Length; p += 16)
        {
            var offset = BinaryPrimitives.ReadInt32LittleEndian(pack[p..]);
            if (offset == 0)
                break;
            var size = BinaryPrimitives.ReadInt32LittleEndian(pack[(p + 4)..]);
            result.Add(new SoundBank(BinaryPrimitives.ReadInt32LittleEndian(pack[(p + 8)..]),
                BinaryPrimitives.ReadInt32LittleEndian(pack[(p + 12)..]), copy.AsMemory(offset, size)));
        }
        return result;
    }

    /// <summary>
    /// Writes a pack as the game's are laid out: the headers (ended by a zero header), the banks
    /// one after another from 0x100 (64-byte aligned), the whole padded to 2048 bytes.
    /// </summary>
    public static byte[] Build(IReadOnlyList<SoundBank> banks)
    {
        static int Align(int v, int a) => (v + a - 1) / a * a;
        var position = Math.Max(0x100, Align((banks.Count + 1) * 16, 64));
        var offsets = new int[banks.Count];
        for (var i = 0; i < banks.Count; i++)
        {
            offsets[i] = position;
            position = Align(position + banks[i].Data.Length, 64);
        }
        var result = new byte[Align(position, 2048)];
        for (var i = 0; i < banks.Count; i++)
        {
            var h = result.AsSpan(i * 16);
            BinaryPrimitives.WriteInt32LittleEndian(h, offsets[i]);
            BinaryPrimitives.WriteInt32LittleEndian(h[4..], banks[i].Data.Length);
            BinaryPrimitives.WriteInt32LittleEndian(h[8..], banks[i].Type);
            BinaryPrimitives.WriteInt32LittleEndian(h[12..], banks[i].BankNo);
            banks[i].Data.Span.CopyTo(result.AsSpan(offsets[i]));
        }
        return result;
    }

    public void Dispose() => _afs.Dispose();
}
