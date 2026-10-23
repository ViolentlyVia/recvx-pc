namespace Cvx.Formats;

/// <summary>
/// PRS, the Sega LZ77 variant used for Code: Veronica's room files.
/// </summary>
/// <remarks>
/// The bit stream follows BioRand's PRS code (itself based on Sewer56's dlang-prs). The match
/// finder here is a hash chain over the full 8 KB window instead of BioRand's 256-byte linear
/// search. A room takes about 3 seconds and comes out about 0.5% larger than Capcom's original
/// (all 205 rooms: 100.6%, worst +30 KB for RM_7100).
///
/// Codes (control bits are read least significant bit first):
/// <list type="bullet">
/// <item>1, byte: literal</item>
/// <item>00, 2 length bits, byte: copy 2-5 bytes from up to 256 back</item>
/// <item>01, 16 bits (offset 13, length 3): copy 3-9 bytes from up to 8191 back</item>
/// <item>01, 16 bits (offset 13, length 0), byte: copy 1-256 bytes from up to 8191 back</item>
/// <item>01, 0x0000: end</item>
/// </list>
/// </remarks>
public static class Prs
{
    private const int MaxOffset = 0x1FFF;
    private const int ShortMaxOffset = 0x100;
    private const int MaxLength = 0x100;
    private const int HashBits = 15;
    private const int MaxChain = 512;

    public static byte[] Decompress(ReadOnlySpan<byte> source) => Expand.Decompress(source);

    // Lazy matching saves only about 0.02% on room files for 50% more time, so it is off.
    public static byte[] Compress(ReadOnlySpan<byte> source) => Compress(source, lazy: false, maxChain: MaxChain);

    internal static byte[] Compress(ReadOnlySpan<byte> source, bool lazy, int maxChain)
    {
        var writer = new BitWriter(source.Length + source.Length / 8 + 16);

        var head = new int[1 << HashBits];
        Array.Fill(head, -1);
        var prev = new int[source.Length];

        var pos = 0;
        var inserted = 0; // every position below this is in the hash chains
        (int Length, int Distance)? pending = null;
        while (pos < source.Length)
        {
            while (inserted < pos)
                Insert(source, inserted++, head, prev);

            var (length, distance) = pending ?? FindMatch(source, pos, head, prev, maxChain);
            pending = null;

            // Lazy matching: if starting one byte later saves more bits, write this byte as a
            // literal and take the later match.
            if (lazy && Saving(length, distance) > 0 && length < MaxLength && pos + 1 < source.Length)
            {
                Insert(source, inserted++, head, prev);
                var next = FindMatch(source, pos + 1, head, prev, maxChain);
                if (Saving(next.Length, next.Distance) > Saving(length, distance))
                {
                    writer.Bit(1);
                    writer.Byte(source[pos]);
                    pos++;
                    pending = next;
                    continue;
                }
            }

            if (length >= 2 && length <= 5 && distance <= ShortMaxOffset)
            {
                writer.Bit(0);
                writer.Bit(0);
                var n = length - 2;
                writer.Bit((n >> 1) & 1);
                writer.Bit(n & 1);
                writer.Byte((byte)(-distance & 0xFF));
            }
            else if (length >= 3)
            {
                writer.Bit(0);
                writer.Bit(1);
                var packedOffset = (-distance << 3) & 0xFFF8;
                if (length <= 9)
                {
                    var packed = packedOffset | (length - 2);
                    writer.Byte((byte)packed);
                    writer.Byte((byte)(packed >> 8));
                }
                else
                {
                    writer.Byte((byte)packedOffset);
                    writer.Byte((byte)(packedOffset >> 8));
                    writer.Byte((byte)(length - 1));
                }
            }
            else
            {
                length = 1;
                writer.Bit(1);
                writer.Byte(source[pos]);
            }

            pos += length;
        }

        writer.Bit(0);
        writer.Bit(1);
        writer.Byte(0);
        writer.Byte(0);
        return writer.ToArray();
    }

    /// <summary>Bits saved by a match compared with writing literals (9 bits per byte).</summary>
    private static int Saving(int length, int distance)
    {
        if (length >= 2 && length <= 5 && distance <= ShortMaxOffset)
            return length * 9 - 12;
        if (length >= 3 && length <= 9)
            return length * 9 - 18;
        if (length >= 10)
            return length * 9 - 26;
        return 0;
    }

    private static int Hash(ReadOnlySpan<byte> s, int pos) =>
        (int)(((uint)(s[pos] << 16 | s[pos + 1] << 8 | s[pos + 2]) * 0x9E3779B1u) >> (32 - HashBits));

    private static void Insert(ReadOnlySpan<byte> s, int pos, int[] head, int[] prev)
    {
        if (pos + 2 >= s.Length)
            return;
        var h = Hash(s, pos);
        prev[pos] = head[h];
        head[h] = pos;
    }

    private static (int Length, int Distance) FindMatch(ReadOnlySpan<byte> s, int pos, int[] head, int[] prev, int maxChain)
    {
        var bestLength = 0;
        var bestDistance = 0;
        var maxLength = Math.Min(MaxLength, s.Length - pos);

        if (pos + 2 < s.Length)
        {
            var candidate = head[Hash(s, pos)];
            var chain = 0;
            while (candidate >= 0 && pos - candidate <= MaxOffset && chain++ < maxChain)
            {
                if (s[candidate + bestLength] == s[pos + bestLength])
                {
                    var length = 0;
                    while (length < maxLength && s[candidate + length] == s[pos + length])
                        length++;
                    if (length > bestLength)
                    {
                        bestLength = length;
                        bestDistance = pos - candidate;
                        if (length == maxLength)
                            break;
                    }
                }
                candidate = prev[candidate];
            }
        }

        // A 3-byte match far away costs 18 bits, a nearby 2-5 byte one 12, so prefer the
        // short form when it covers the same length.
        if (bestLength <= 5 && maxLength >= 2)
        {
            var limit = Math.Min(Math.Min(5, maxLength), Math.Max(bestLength, 2));
            var start = Math.Max(0, pos - ShortMaxOffset);
            for (var candidate = pos - 1; candidate >= start; candidate--)
            {
                var length = 0;
                while (length < limit && s[candidate + length] == s[pos + length])
                    length++;
                if (length >= 2 && length >= bestLength && (length > bestLength || bestDistance > ShortMaxOffset))
                {
                    bestLength = length;
                    bestDistance = pos - candidate;
                    if (length == limit)
                        break;
                }
            }
        }

        return (bestLength, bestDistance);
    }

    private sealed class BitWriter
    {
        private byte[] _buffer;
        private int _length;
        private int _controlIndex = -1;
        private int _bit = 8;

        public BitWriter(int capacity) => _buffer = new byte[Math.Max(capacity, 16)];

        // A new control byte is started just before the code that needs it, so a code and
        // its data bytes always follow the control byte they belong to.
        public void Bit(int value)
        {
            if (_bit == 8)
            {
                _controlIndex = _length;
                Byte(0);
                _bit = 0;
            }
            _buffer[_controlIndex] |= (byte)(value << _bit);
            _bit++;
        }

        public void Byte(byte value)
        {
            if (_length == _buffer.Length)
                Array.Resize(ref _buffer, _buffer.Length * 2);
            _buffer[_length++] = value;
        }

        public byte[] ToArray() => _buffer.AsSpan(0, _length).ToArray();
    }
}
