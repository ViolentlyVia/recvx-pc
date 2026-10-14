using System.Buffers.Binary;
using Cvx.Formats;

namespace Cvx.Tests;

public class AfsTests
{
    private static byte[] MakeArchive(params byte[][] files)
    {
        const int dataStart = 0x800;
        using var ms = new MemoryStream();
        var header = new byte[dataStart];
        "AFS\0"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), files.Length);
        long offset = dataStart;
        for (var i = 0; i < files.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8 + i * 8), (uint)offset);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12 + i * 8), (uint)files[i].Length);
            offset = (offset + files[i].Length + 2047) / 2048 * 2048;
        }
        ms.Write(header);
        foreach (var f in files)
        {
            ms.Write(f);
            ms.Write(new byte[(2048 - f.Length % 2048) % 2048]);
        }
        return ms.ToArray();
    }

    private static byte[] Bytes(int length, byte seed) =>
        Enumerable.Range(0, length).Select(i => (byte)(i * 7 + seed)).ToArray();

    [Fact]
    public void ReadsEntries()
    {
        var files = new[] { Bytes(10, 1), Bytes(3000, 2), Bytes(2048, 3) };
        using var afs = AfsArchive.FromStream(new MemoryStream(MakeArchive(files)));
        Assert.Equal(3, afs.Count);
        for (var i = 0; i < files.Length; i++)
            Assert.Equal(files[i], afs.ReadEntry(i));
    }

    [Fact]
    public void RewriteWithoutChangesIsIdentical()
    {
        var original = MakeArchive(Bytes(10, 1), Bytes(3000, 2), Bytes(2048, 3));
        using var afs = AfsArchive.FromStream(new MemoryStream(original));
        using var output = new MemoryStream();
        AfsWriter.Write(afs, new Dictionary<int, byte[]>(), output);
        Assert.Equal(original, output.ToArray());
    }

    [Fact]
    public void ReplacingAnEntryRepacksTheRest()
    {
        var files = new[] { Bytes(10, 1), Bytes(3000, 2), Bytes(2048, 3) };
        using var afs = AfsArchive.FromStream(new MemoryStream(MakeArchive(files)));
        var bigger = Bytes(9000, 9);
        using var output = new MemoryStream();
        AfsWriter.Write(afs, new Dictionary<int, byte[]> { [1] = bigger }, output);

        output.Position = 0;
        using var result = AfsArchive.FromStream(output);
        Assert.Equal(files[0], result.ReadEntry(0));
        Assert.Equal(bigger, result.ReadEntry(1));
        Assert.Equal(files[2], result.ReadEntry(2));

        // Packed back to back on sector boundaries, as the game's gdFsInit expects.
        var expected = result.Entries[0].Offset;
        foreach (var e in result.Entries)
        {
            Assert.Equal(expected, e.Offset);
            expected = (e.Offset + e.Size + 2047) / 2048 * 2048;
        }
        Assert.Equal(expected, output.Length);
    }

    [Fact]
    public void RejectsNonAfs()
    {
        Assert.Throws<InvalidDataException>(() => AfsArchive.FromStream(new MemoryStream(new byte[4096])));
    }
}
