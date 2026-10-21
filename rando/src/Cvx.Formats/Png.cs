using System.Buffers.Binary;
using System.IO.Compression;

namespace Cvx.Formats;

/// <summary>A minimal PNG encoder and decoder (8-bit RGBA, and 8-bit palette or RGBA in), for textures.</summary>
public static class Png
{
    private static ReadOnlySpan<byte> Signature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++)
            c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    /// <summary>Writes an 8-bit RGBA image.</summary>
    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba)
    {
        using var ms = new MemoryStream();
        ms.Write(Signature);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;  // bit depth
        header[9] = 6;  // RGBA
        WriteChunk(ms, "IHDR", header);

        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (var y = 0; y < height; y++)
            {
                z.WriteByte(0);
                z.Write(rgba.Slice(y * width * 4, width * 4));
            }
        }
        WriteChunk(ms, "IDAT", raw.ToArray());
        WriteChunk(ms, "IEND", []);
        return ms.ToArray();
    }

    /// <summary>
    /// Reads a PNG as RGBA: 8-bit greyscale, RGB, RGBA or palette images (not interlaced),
    /// the kinds BioRand's portraits and textures use.
    /// </summary>
    public static (int Width, int Height, byte[] Rgba) Decode(ReadOnlySpan<byte> png)
    {
        if (!png.StartsWith(Signature))
            throw new InvalidDataException("Not a PNG file.");
        int width = 0, height = 0, depth = 0, color = 0;
        byte[] palette = [], alpha = [];
        using var idat = new MemoryStream();
        for (var p = 8; p + 8 <= png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png[p..]);
            var type = System.Text.Encoding.ASCII.GetString(png.Slice(p + 4, 4));
            var data = png.Slice(p + 8, length);
            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(data);
                    height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                    depth = data[8];
                    color = data[9];
                    if (data[12] != 0)
                        throw new NotSupportedException("Interlaced PNGs are not supported.");
                    break;
                case "PLTE": palette = data.ToArray(); break;
                case "tRNS": alpha = data.ToArray(); break;
                case "IDAT": idat.Write(data); break;
            }
            p += 12 + length;
        }
        if (depth != 8)
            throw new NotSupportedException($"PNG bit depth {depth} is not supported.");
        var channels = color switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new NotSupportedException($"PNG colour type {color}.") };

        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        var stride = width * channels;
        var rows = new byte[height * stride];
        var prior = new byte[stride];
        var line = new byte[stride + 1];
        for (var y = 0; y < height; y++)
        {
            z.ReadExactly(line);
            var row = rows.AsSpan(y * stride, stride);
            for (var x = 0; x < stride; x++)
            {
                int a = x >= channels ? row[x - channels] : 0, b = prior[x], c = x >= channels ? prior[x - channels] : 0;
                var f = line[1 + x];
                row[x] = (byte)(line[0] switch
                {
                    0 => f,
                    1 => f + a,
                    2 => f + b,
                    3 => f + (a + b) / 2,
                    4 => f + Paeth(a, b, c),
                    _ => throw new InvalidDataException("Bad PNG filter."),
                });
            }
            row.CopyTo(prior);
        }

        var rgba = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            var s = rows.AsSpan(i * channels, channels);
            var d = rgba.AsSpan(i * 4, 4);
            switch (color)
            {
                case 0: d[0] = d[1] = d[2] = s[0]; d[3] = 255; break;
                case 2: s.CopyTo(d); d[3] = 255; break;
                case 3:
                    palette.AsSpan(s[0] * 3, 3).CopyTo(d);
                    d[3] = s[0] < alpha.Length ? alpha[s[0]] : (byte)255;
                    break;
                case 4: d[0] = d[1] = d[2] = s[0]; d[3] = s[1]; break;
                case 6: s.CopyTo(d); break;
            }
        }
        return (width, height, rgba);
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void WriteChunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        s.Write(word);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        var crc = 0xFFFFFFFF;
        foreach (var b in typeBytes)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (var b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        BinaryPrimitives.WriteUInt32BigEndian(word, crc ^ 0xFFFFFFFF);
        s.Write(word);
    }
}
