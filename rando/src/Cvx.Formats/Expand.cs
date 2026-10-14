namespace Cvx.Formats;

/// <summary>
/// The game's own room decompressor (<c>Expand()</c> in recvx-pc's expand.c, C version by
/// Clownacy), with bounds checks added. Used to prove that what <see cref="Prs.Compress"/>
/// writes is exactly what the game will read back.
/// </summary>
public static class Expand
{
    public static byte[] Decompress(ReadOnlySpan<byte> source, int capacityHint = 0)
    {
        var dst = new byte[Math.Max(capacityHint, source.Length * 4 + 256)];
        var d = 0;
        var s = 0;

        int descriptor = source[s++];
        // The game starts with one extra bit remaining; together with the pre-decrement in
        // GetBit this reads all 8 bits of the first descriptor byte.
        var bitsRemaining = 9;

        int GetBit(ReadOnlySpan<byte> src)
        {
            if (--bitsRemaining == 0)
            {
                descriptor = src[s++];
                bitsRemaining = 8;
            }
            var bit = descriptor & 1;
            descriptor >>= 1;
            return bit;
        }

        while (true)
        {
            if (GetBit(source) != 0)
            {
                Ensure(ref dst, d + 1);
                dst[d++] = source[s++];
                continue;
            }

            int length;
            int offset;
            if (GetBit(source) == 0)
            {
                length = 0;
                if (GetBit(source) != 0)
                    length += 2;
                if (GetBit(source) != 0)
                    length += 1;
                length += 1;
                offset = -0x100 + source[s++];
            }
            else
            {
                int lower = source[s++];
                int upper = source[s++];
                var whole = lower | (upper << 8);
                if (whole == 0)
                    break;

                offset = -0x2000 + (whole >> 3);
                length = whole & 7;
                if (length != 0)
                    length += 1;
                else
                    length = source[s++];
            }

            // do { ... } while (length-- != 0) copies length + 1 bytes.
            var count = length + 1;
            if (d + offset < 0)
                throw new InvalidDataException("Back reference before the start of the output.");
            Ensure(ref dst, d + count);
            for (var i = 0; i < count; i++, d++)
                dst[d] = dst[d + offset];
        }

        Array.Resize(ref dst, d);
        return dst;
    }

    private static void Ensure(ref byte[] buffer, int length)
    {
        if (length > buffer.Length)
            Array.Resize(ref buffer, Math.Max(length, buffer.Length * 2));
    }
}
