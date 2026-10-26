namespace Cvx.Formats;

/// <summary>
/// SPU ADPCM ("VAG" data, the samples of the sound banks' BD bodies): 16-byte blocks of 28
/// samples, a head byte (filter in the high nibble, shift in the low) and a flag byte (bit 0:
/// the sample ends here; 2: repeat; 4: loop start), then 14 bytes of 4-bit deltas. The game's
/// samples start with a silent block and end with flag 1 (pc_iop_tsnddrv.c decodes them).
/// </summary>
public static class Vag
{
    public const int BlockSize = 16;
    public const int SamplesPerBlock = 28;

    // the SPU's prediction filters, in 64ths
    private static readonly (int A, int B)[] Filters = [(0, 0), (60, 0), (115, -52), (98, -55), (122, -60)];

    /// <summary>One-shot data: a silent block, the samples, the last block flagged as the end.</summary>
    public static byte[] Encode(ReadOnlySpan<short> pcm)
    {
        var blocks = (pcm.Length + SamplesPerBlock - 1) / SamplesPerBlock;
        var output = new byte[(blocks + 1) * BlockSize];
        int s1 = 0, s2 = 0;
        var buffer = new short[SamplesPerBlock];
        for (var b = 0; b < blocks; b++)
        {
            buffer.AsSpan().Clear();
            var n = Math.Min(SamplesPerBlock, pcm.Length - b * SamplesPerBlock);
            pcm.Slice(b * SamplesPerBlock, n).CopyTo(buffer);
            var block = output.AsSpan((b + 1) * BlockSize, BlockSize);
            (s1, s2) = EncodeBlock(buffer, block, s1, s2);
            block[1] = (byte)(b == blocks - 1 ? 1 : 0);
        }
        if (blocks == 0)
            output[1] = 1;
        return output;
    }

    /// <summary>Picks the filter and shift with the least error, then writes the block with them.</summary>
    private static (int S1, int S2) EncodeBlock(short[] samples, Span<byte> block, int s1, int s2)
    {
        var bestError = double.MaxValue;
        int bestFilter = 0, bestShift = 0;
        for (var f = 0; f < Filters.Length; f++)
        {
            for (var shift = 0; shift <= 12; shift++)
            {
                var (_, _, error) = Run(samples, f, shift, s1, s2, null);
                if (error < bestError)
                {
                    bestError = error;
                    bestFilter = f;
                    bestShift = shift;
                }
            }
        }
        block[0] = (byte)(bestFilter << 4 | bestShift);
        var nibbles = new byte[SamplesPerBlock];
        var (n1, n2, _) = Run(samples, bestFilter, bestShift, s1, s2, nibbles);
        for (var i = 0; i < 14; i++)
            block[2 + i] = (byte)(nibbles[2 * i] | nibbles[2 * i + 1] << 4);
        return (n1, n2);
    }

    /// <summary>Encodes a block with one filter and shift as the SPU decodes it: the error, and the predictor's state after.</summary>
    private static (int S1, int S2, double Error) Run(short[] samples, int filter, int shift, int s1, int s2, byte[]? nibbles)
    {
        var (a, b) = Filters[filter];
        double error = 0;
        for (var i = 0; i < SamplesPerBlock; i++)
        {
            var predicted = (s1 * a + s2 * b + 32) >> 6;
            var delta = samples[i] - predicted;
            // the nibble: a signed 4-bit value the SPU turns back into (q << 12) >> shift
            var q = Math.Clamp((int)Math.Round(delta * (1 << shift) / 4096.0), -8, 7);
            var decoded = Math.Clamp(((q << 12) >> shift) + predicted, short.MinValue, short.MaxValue);
            var e = samples[i] - decoded;
            error += (double)e * e;
            if (nibbles != null)
                nibbles[i] = (byte)(q & 0xF);
            s2 = s1;
            s1 = decoded;
        }
        return (s1, s2, error);
    }

    /// <summary>Decodes from <paramref name="offset"/> to the block flagged as the end (or the end of the data).</summary>
    public static short[] Decode(ReadOnlySpan<byte> data, int offset = 0)
    {
        var output = new List<short>();
        int s1 = 0, s2 = 0;
        for (var p = offset; p + BlockSize <= data.Length; p += BlockSize)
        {
            var head = data[p];
            var (a, b) = Filters[Math.Min(head >> 4, Filters.Length - 1)];
            var shift = head & 0xF;
            for (var i = 0; i < SamplesPerBlock; i++)
            {
                var nibble = (data[p + 2 + i / 2] >> (i % 2 * 4)) & 0xF;
                var q = (nibble << 28) >> 28;
                var predicted = (s1 * a + s2 * b + 32) >> 6;
                var decoded = Math.Clamp(((q << 12) >> shift) + predicted, short.MinValue, short.MaxValue);
                output.Add((short)decoded);
                s2 = s1;
                s1 = decoded;
            }
            if ((data[p + 1] & 1) != 0)
                break;
        }
        return output.ToArray();
    }
}
