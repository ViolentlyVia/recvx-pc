namespace Cvx.Formats;

/// <summary>
/// The game's own room decompressor (<c>Expand()</c> in recvx-pc's expand.c, C version by
/// Clownacy), with bounds checks added. Used to prove that what <see cref="Prs.Compress"/>
/// writes is exactly what the game will read back.
/// </summary>
public static class Expand
{
    /// <summary>
    /// The room memory pool of recvx-pc (bytes from <c>sys->memp</c> to <c>sys->endp</c> when a
    /// room loads, from its <c>room ...: pool</c> log line). The packed room is read to the top
    /// of the pool and expanded from the bottom, in place.
    /// </summary>
    public const int RoomPool = 11160448;

    /// <summary>Where the game puts a packed room of <paramref name="packedSize"/> bytes, from the pool's start.</summary>
    public static int PackedOffset(int packedSize) => (RoomPool - packedSize) & ~63;

    /// <summary>
    /// Whether the game can expand <paramref name="source"/> in place: the output never reaches
    /// packed bytes the decompressor has not read yet, and the room fits the pool.
    /// </summary>
    public static bool FitsRoomPool(ReadOnlySpan<byte> source, out int expandedSize, out int spare)
    {
        var data = Decompress(source, 0, out var maxLead);
        expandedSize = data.Length;
        spare = PackedOffset(source.Length) - maxLead - 1;
        return spare >= 0 && expandedSize <= RoomPool;
    }

    public static byte[] Decompress(ReadOnlySpan<byte> source, int capacityHint = 0) => Decompress(source, capacityHint, out _);

    /// <param name="maxLead">The most the output was ahead of the input when a byte was written (output position minus input bytes read).</param>
    public static byte[] Decompress(ReadOnlySpan<byte> source, int capacityHint, out int maxLead)
    {
        var lead = 0;
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
                var b = source[s++];
                lead = Math.Max(lead, d - s);
                dst[d++] = b;
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
            lead = Math.Max(lead, d + count - 1 - s);
            for (var i = 0; i < count; i++, d++)
                dst[d] = dst[d + offset];
        }

        Array.Resize(ref dst, d);
        maxLead = lead;
        return dst;
    }

    private static void Ensure(ref byte[] buffer, int length)
    {
        if (length > buffer.Length)
            Array.Resize(ref buffer, Math.Max(length, buffer.Length * 2));
    }
}
