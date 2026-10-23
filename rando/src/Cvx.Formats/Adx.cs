using System.Buffers.Binary;

namespace Cvx.Formats;

/// <summary>
/// CRI ADX (type 3, 4-bit ADPCM, 18-byte blocks of 32 samples per channel), as the game's own
/// decoder reads it (recvx-pc libadxe: <c>ADX_DecodeInfo</c>, <c>ADX_DecodeInfoExLoop</c>,
/// <c>ADX_DecodeMonoFloat</c>). Used to put other games' music into BGM1.AFS/BGM2.AFS.
/// </summary>
public static class Adx
{
    public const int BlockSize = 18;
    public const int SamplesPerBlock = 32;
    /// <summary>The high-pass frequency every track of the game uses for its predictor.</summary>
    public const int Cutoff = 500;
    /// <summary>Where the samples start: one sector, so the loop start is sector-aligned like the game's tracks.</summary>
    public const int DataStart = 2048;

    /// <summary>The predictor coefficients (ADX_GetCoefficient), in 4096ths.</summary>
    public static (int K0, int K1) Coefficients(int sampleRate)
    {
        var val1 = MathF.Sqrt(2) - MathF.Cos(2 * MathF.PI * Cutoff / sampleRate);
        var val2 = MathF.Sqrt(2) - 1;
        var r = (val1 - MathF.Sqrt((val1 + val2) * (val1 - val2))) / val2;
        return ((short)(2 * r * 4096), (short)(-r * r * 4096));
    }

    /// <summary>
    /// Encodes 16-bit PCM (one array per channel, all the same length) as a version 3 ADX that
    /// loops over the whole track, or plays once (<paramref name="loop"/> false: the title call).
    /// </summary>
    public static byte[] Encode(IReadOnlyList<short[]> channels, int sampleRate, bool loop = true)
    {
        var nch = channels.Count;
        if (nch is < 1 or > 2)
            throw new ArgumentException("One or two channels.", nameof(channels));
        var samples = channels[0].Length;
        if (samples == 0 || channels.Any(c => c.Length != samples))
            throw new ArgumentException("The channels need the same, non-zero length.", nameof(channels));

        var blocks = (samples + SamplesPerBlock - 1) / SamplesPerBlock;
        var dataEnd = DataStart + blocks * BlockSize * nch;
        // the end marker block, then zeros to a sector boundary
        var length = (dataEnd + BlockSize + AfsArchive.SectorSize - 1) / AfsArchive.SectorSize * AfsArchive.SectorSize;
        var output = new byte[length];
        var o = output.AsSpan();

        BinaryPrimitives.WriteUInt16BigEndian(o, 0x8000);
        BinaryPrimitives.WriteUInt16BigEndian(o[2..], DataStart - 4);   // the copyright text ends here
        o[4] = 3;                                                        // ADX ADPCM
        o[5] = BlockSize;
        o[6] = 4;                                                        // bits per sample
        o[7] = (byte)nch;
        BinaryPrimitives.WriteInt32BigEndian(o[8..], sampleRate);
        BinaryPrimitives.WriteInt32BigEndian(o[0xC..], samples);
        BinaryPrimitives.WriteUInt16BigEndian(o[0x10..], Cutoff);
        o[0x12] = 3;                                                     // version 3: loop info at 0x14
        if (loop)
        {
            BinaryPrimitives.WriteInt16BigEndian(o[0x14..], 0);          // no samples inserted before the loop
            BinaryPrimitives.WriteInt16BigEndian(o[0x16..], 1);          // one loop
            BinaryPrimitives.WriteInt32BigEndian(o[0x18..], 1);          // looping
            BinaryPrimitives.WriteInt32BigEndian(o[0x1C..], 0);          // from the first sample
            BinaryPrimitives.WriteInt32BigEndian(o[0x20..], DataStart);
            BinaryPrimitives.WriteInt32BigEndian(o[0x24..], samples);    // to the last
            BinaryPrimitives.WriteInt32BigEndian(o[0x28..], dataEnd);
        }
        "(c)CRI"u8.CopyTo(o[(DataStart - 6)..]);

        var (k0, k1) = Coefficients(sampleRate);
        var block = new short[SamplesPerBlock];
        for (var ch = 0; ch < nch; ch++)
        {
            var pcm = channels[ch];
            double h1 = 0, h2 = 0;
            for (var b = 0; b < blocks; b++)
            {
                var count = Math.Min(SamplesPerBlock, samples - b * SamplesPerBlock);
                Array.Clear(block);
                Array.Copy(pcm, b * SamplesPerBlock, block, 0, count);
                var at = DataStart + (b * nch + ch) * BlockSize;
                (h1, h2) = EncodeBlock(block, o.Slice(at, BlockSize), k0, k1, h1, h2);
            }
        }

        // end marker: a block whose scale has the top bit set
        BinaryPrimitives.WriteUInt16BigEndian(o[dataEnd..], 0x8001);
        BinaryPrimitives.WriteUInt16BigEndian(o[(dataEnd + 2)..], BlockSize - 4);
        return output;
    }

    /// <summary>
    /// One block: tries a few gains around the one the open-loop prediction suggests and keeps
    /// the one with the least error once decoded. Returns the decoder's history after it.
    /// </summary>
    private static (double H1, double H2) EncodeBlock(short[] pcm, Span<byte> dest, int k0, int k1, double h1, double h2)
    {
        // how far the samples are from the prediction (starting from the decoder's history)
        double lo = 0, hi = 0, p1 = h1, p2 = h2;
        foreach (var s in pcm)
        {
            var d = s - Prediction(k0, k1, p1, p2);
            lo = Math.Min(lo, d);
            hi = Math.Max(hi, d);
            p2 = p1;
            p1 = s;
        }
        var guess = Math.Max(1, (int)Math.Ceiling(Math.Max(hi / 7, -lo / 8)));

        Span<byte> best = stackalloc byte[SamplesPerBlock];
        Span<byte> codes = stackalloc byte[SamplesPerBlock];
        var bestError = double.MaxValue;
        var bestGain = guess;
        double bestH1 = h1, bestH2 = h2;
        foreach (var factor in (ReadOnlySpan<double>)[0.8, 0.9, 1.0, 1.15])
        {
            var gain = Math.Clamp((int)Math.Round(guess * factor), 1, 0x2000);
            double error = 0;
            double s1 = h1, s2 = h2;
            for (var i = 0; i < SamplesPerBlock; i++)
            {
                var prediction = Prediction(k0, k1, s1, s2);
                var q = (int)Math.Clamp(Math.Round((pcm[i] - prediction) / gain), -8, 7);
                var decoded = prediction + q * gain;
                codes[i] = (byte)(q & 0xF);
                var e = pcm[i] - Clamp16(decoded);
                error += e * e;
                s2 = s1;
                s1 = decoded;
            }
            if (error < bestError)
            {
                bestError = error;
                bestGain = gain;
                bestH1 = s1;
                bestH2 = s2;
                codes.CopyTo(best);
            }
        }

        BinaryPrimitives.WriteUInt16BigEndian(dest, (ushort)(bestGain - 1));
        for (var i = 0; i < SamplesPerBlock; i += 2)
            dest[2 + i / 2] = (byte)((best[i] << 4) | best[i + 1]);
        return (bestH1, bestH2);
    }

    // The decoder keeps its history unrounded (16.16 floats) and only clamps what it outputs.
    private static double Prediction(int k0, int k1, double s1, double s2) => (k0 * s1 + k1 * s2) / 4096.0;

    private static int Clamp16(double v) => (int)Math.Clamp(Math.Truncate(v), short.MinValue, short.MaxValue);

    /// <summary>Decodes an ADX (any version with type 3 blocks) to 16-bit PCM, one array per channel.</summary>
    public static (short[][] Channels, int SampleRate) Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 0x14 || BinaryPrimitives.ReadUInt16BigEndian(data) != 0x8000)
            throw new InvalidDataException("Not an ADX file.");
        var start = BinaryPrimitives.ReadUInt16BigEndian(data[2..]) + 4;
        var blockSize = data[5];
        var nch = data[7];
        var rate = BinaryPrimitives.ReadInt32BigEndian(data[8..]);
        var samples = BinaryPrimitives.ReadInt32BigEndian(data[0xC..]);
        var cutoff = BinaryPrimitives.ReadUInt16BigEndian(data[0x10..]);
        if (data[4] != 3 || blockSize != BlockSize || nch is < 1 or > 2)
            throw new InvalidDataException("Unsupported ADX.");

        // ADX_GetCoefficient with this file's cutoff
        var val1 = MathF.Sqrt(2) - MathF.Cos(2 * MathF.PI * cutoff / rate);
        var val2 = MathF.Sqrt(2) - 1;
        var r = (val1 - MathF.Sqrt((val1 + val2) * (val1 - val2))) / val2;
        int k0 = (short)(2 * r * 4096), k1 = (short)(-r * r * 4096);

        var output = new short[nch][];
        for (var ch = 0; ch < nch; ch++)
        {
            output[ch] = new short[samples];
            double s1 = 0, s2 = 0;
            for (var n = 0; n < samples; n++)
            {
                var b = n / SamplesPerBlock;
                var at = start + (b * nch + ch) * BlockSize;
                if (at + BlockSize > data.Length)
                    break;
                var code = BinaryPrimitives.ReadUInt16BigEndian(data[at..]);
                if ((code & 0x8000) != 0)
                    break;
                var gain = (code & 0x1FFF) + 1;
                var i = n % SamplesPerBlock;
                var nibble = (data[at + 2 + i / 2] >> ((i & 1) == 0 ? 4 : 0)) & 0xF;
                var q = nibble >= 8 ? nibble - 16 : nibble;
                var sample = Prediction(k0, k1, s1, s2) + q * gain;
                output[ch][n] = (short)Clamp16(sample);
                s2 = s1;
                s1 = sample;
            }
        }
        return (output, rate);
    }
}
