using Cvx.Formats;

namespace Cvx.Tests;

public class PrsTests
{
    private static void AssertRoundTrip(byte[] data)
    {
        var compressed = Prs.Compress(data);
        var expanded = Expand.Decompress(compressed);
        Assert.Equal(data, expanded);
    }

    [Fact]
    public void Empty() => AssertRoundTrip([]);

    [Fact]
    public void SingleByte() => AssertRoundTrip([0x42]);

    [Fact]
    public void LongRun()
    {
        // Runs longer than the 256-byte maximum copy, and an overlapping copy at distance 1.
        AssertRoundTrip(Enumerable.Repeat((byte)0xAA, 5000).ToArray());
    }

    [Fact]
    public void RandomData()
    {
        var rng = new Random(1234);
        var data = new byte[100_000];
        rng.NextBytes(data);
        AssertRoundTrip(data);
    }

    [Fact]
    public void MixedRepeatsAtAllDistances()
    {
        // Repeats at short (<= 256) and long (up to 8191) distances, and just past the window.
        var rng = new Random(99);
        var data = new List<byte>();
        var block = new byte[64];
        rng.NextBytes(block);
        foreach (var gap in new[] { 1, 2, 5, 200, 255, 256, 257, 4000, 8100, 8191, 8192, 9000 })
        {
            data.AddRange(block);
            var filler = new byte[gap];
            rng.NextBytes(filler);
            data.AddRange(filler);
            data.AddRange(block.AsSpan(0, 2 + gap % 30).ToArray());
        }
        AssertRoundTrip(data.ToArray());
    }

    [Fact]
    public void CompressesRepetitiveData()
    {
        var data = Enumerable.Range(0, 20_000).Select(i => (byte)(i % 37)).ToArray();
        var compressed = Prs.Compress(data);
        Assert.True(compressed.Length < data.Length / 20, $"{compressed.Length} bytes");
    }

    [Fact]
    public void ExpandReadsHandWrittenStream()
    {
        // First control byte, bits read from the lowest: 1 1 1 (literals A B C), 0 0 (short
        // copy) 1 1 (copy 5) with offset byte 0xFD (3 back) -> "ABCABCAB", then 1 (literal D).
        // Second control byte: 0 1 (long copy) with 0x0000, which is the end code.
        byte[] stream =
        [
            0b1110_0111, (byte)'A', (byte)'B', (byte)'C', 0xFD, (byte)'D',
            0b0000_0010, 0x00, 0x00,
        ];
        Assert.Equal("ABCABCABD"u8.ToArray(), Expand.Decompress(stream));
    }
}
