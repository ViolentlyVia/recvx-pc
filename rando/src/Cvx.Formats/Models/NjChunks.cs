using System.Buffers.Binary;
using System.Numerics;

namespace Cvx.Formats.Models;

/// <summary>
/// A chunk of a model's vertex list (<c>vlist</c>): type, head bits and the words after the
/// 4-byte head (the size field counts them). The vertex list ends with type 0xFF.
/// </summary>
/// <remarks>
/// Code: Veronica's PS2 models use type 51 (<c>njCnkCvVnPs2</c>, ps2_NinjaCnk.c): after the
/// index offset and count, 56 bytes of padding (the head is a 64-byte <c>HDR_PS</c>, njplus.h),
/// then each vertex as two float4: position (w = 1) and normal (w = 1). The vertices go to the
/// vertex buffer at the index offset, where the strips find them.
/// </remarks>
public sealed record NjVertexChunk(byte Type, byte HeadBits, byte[] Body)
{
    public const byte Ps2Type = 51;
    private const int Ps2Padding = 56;

    public int IndexOffset => BinaryPrimitives.ReadUInt16LittleEndian(Body);
    public int Count => BinaryPrimitives.ReadUInt16LittleEndian(Body.AsSpan(2));

    public bool IsPs2 => Type == Ps2Type;

    /// <summary>The vertices of a type 51 chunk.</summary>
    public IEnumerable<(Vector3 Position, Vector3 Normal)> Ps2Vertices()
    {
        if (!IsPs2)
            throw new InvalidOperationException($"Vertex chunk type {Type} is not the PS2 type.");
        for (var i = 0; i < Count; i++)
        {
            var at = 4 + Ps2Padding + i * 32;
            yield return (ReadVector(Body.AsSpan(at)), ReadVector(Body.AsSpan(at + 16)));
        }
    }

    /// <summary>A type 51 chunk with these vertices from <paramref name="indexOffset"/> on.</summary>
    public static NjVertexChunk Ps2(IReadOnlyList<(Vector3 Position, Vector3 Normal)> vertices, int indexOffset = 0)
    {
        var body = new byte[4 + Ps2Padding + vertices.Count * 32];
        BinaryPrimitives.WriteUInt16LittleEndian(body, (ushort)indexOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), (ushort)vertices.Count);
        for (var i = 0; i < vertices.Count; i++)
        {
            var at = 4 + Ps2Padding + i * 32;
            WriteVector(body.AsSpan(at), vertices[i].Position);
            WriteVector(body.AsSpan(at + 16), vertices[i].Normal);
        }
        return new NjVertexChunk(Ps2Type, 0, body);
    }

    private static Vector3 ReadVector(ReadOnlySpan<byte> s) => new(
        BinaryPrimitives.ReadSingleLittleEndian(s),
        BinaryPrimitives.ReadSingleLittleEndian(s[4..]),
        BinaryPrimitives.ReadSingleLittleEndian(s[8..]));

    private static void WriteVector(Span<byte> s, Vector3 v)
    {
        BinaryPrimitives.WriteSingleLittleEndian(s, v.X);
        BinaryPrimitives.WriteSingleLittleEndian(s[4..], v.Y);
        BinaryPrimitives.WriteSingleLittleEndian(s[8..], v.Z);
        BinaryPrimitives.WriteSingleLittleEndian(s[12..], 1);
    }
}

public enum NjPolyKind { Null, Bits, Tiny, Material, Volume, Strip, Unknown }

/// <summary>
/// A chunk of a model's polygon list (<c>plist</c>), as the Ninja chunk format defines them
/// (ninjacnk.h) and <c>pCnkFuncTbl</c> (ps2_NinjaCnk.c) reads them: type, head bits, then
/// the body (for chunks with a size field, the 16-bit words it counts). The list ends with
/// type 0xFF.
/// </summary>
/// <remarks>
/// Kinds by type: 0 null and 1-7 bits (head only), 8-15 tiny (one more 16-bit value: texture
/// id), 16-31 material (size, then ARGB colours), 56-63 volume and 64-75 strips (size, then
/// the strips).
/// </remarks>
public sealed record NjPolyChunk(byte Type, byte HeadBits, byte[] Body)
{
    public const byte StripType = 64;
    public const byte StripUvnType = 65;
    public const byte StripUvhType = 66;
    public const byte TextureType = 8;
    public const byte MaterialDiffuseAmbientType = 19;

    public NjPolyKind Kind => KindOf(Type);

    public static NjPolyKind KindOf(byte type) => type switch
    {
        0 => NjPolyKind.Null,
        < 8 => NjPolyKind.Bits,
        < 16 => NjPolyKind.Tiny,
        < 32 => NjPolyKind.Material,
        >= 56 and < 64 => NjPolyKind.Volume,
        >= 64 and < 76 => NjPolyKind.Strip,
        _ => NjPolyKind.Unknown,
    };

    /// <summary>A tiny chunk's value: for type 8 (texture), the texture number in the low 13 bits.</summary>
    public int TinyValue => BinaryPrimitives.ReadUInt16LittleEndian(Body);

    /// <summary>The texture a type 8 chunk selects (<c>njCnkCtTid</c>: the low 12 bits).</summary>
    public int TextureId => TinyValue & 0xFFF;

    /// <summary>A strip chunk's strips (types 64-66; others have per-vertex data this does not read).</summary>
    public IReadOnlyList<NjStrip> Strips() => NjStrip.Read(this);

    public static NjPolyChunk Texture(int id, byte headBits = 0x34, int filter = 1) =>
        new(TextureType, headBits, BitConverter.GetBytes((ushort)(id | filter << 14)));

    /// <summary>A diffuse and ambient material (type 19), each colour as ARGB.</summary>
    public static NjPolyChunk Material(uint diffuse, uint ambient, byte headBits = 0x25)
    {
        var body = new byte[8];
        WriteColor(body, diffuse);
        WriteColor(body.AsSpan(4), ambient);
        return new NjPolyChunk(MaterialDiffuseAmbientType, headBits, body);
    }

    // Ninja stores colours as B, G, R, A bytes
    private static void WriteColor(Span<byte> s, uint argb) =>
        BinaryPrimitives.WriteUInt32LittleEndian(s, argb);
}

/// <summary>
/// One triangle strip of a strip chunk. A negative length in the file flips the winding
/// (<see cref="Flipped"/>). UVs are 0-255 per texture size for type 65 and 0-1023 for 66.
/// </summary>
public sealed record NjStrip(bool Flipped, IReadOnlyList<NjStripVertex> Vertices)
{
    public static IReadOnlyList<NjStrip> Read(NjPolyChunk chunk)
    {
        if (chunk.Kind != NjPolyKind.Strip || chunk.Type > NjPolyChunk.StripUvhType)
            throw new InvalidOperationException($"Chunk type {chunk.Type} is not a plain or UV strip.");
        var uv = chunk.Type != NjPolyChunk.StripType;
        var s = chunk.Body.AsSpan();
        var head = BinaryPrimitives.ReadUInt16LittleEndian(s);
        var count = head & 0x3FFF;
        var user = head >> 14;
        var p = 2;
        var result = new List<NjStrip>(count);
        for (var i = 0; i < count; i++)
        {
            var length = BinaryPrimitives.ReadInt16LittleEndian(s[p..]);
            p += 2;
            var vertices = new List<NjStripVertex>(Math.Abs((int)length));
            for (var v = 0; v < Math.Abs((int)length); v++)
            {
                var index = BinaryPrimitives.ReadUInt16LittleEndian(s[p..]);
                p += 2;
                short u = 0, w = 0;
                if (uv)
                {
                    u = BinaryPrimitives.ReadInt16LittleEndian(s[p..]);
                    w = BinaryPrimitives.ReadInt16LittleEndian(s[(p + 2)..]);
                    p += 4;
                }
                // user data follows every vertex after the first two (njCnkCs)
                var userData = v >= 2 ? s.Slice(p, user * 2).ToArray() : [];
                p += v >= 2 ? user * 2 : 0;
                vertices.Add(new NjStripVertex(index, u, w, userData));
            }
            result.Add(new NjStrip(length < 0, vertices));
        }
        return result;
    }

    /// <summary>
    /// A strip chunk with these strips. <paramref name="type"/> is 64 (no UVs), 65 or 66;
    /// the body is padded to whole 32-bit words like the game's.
    /// </summary>
    public static NjPolyChunk Write(byte type, byte headBits, IReadOnlyList<NjStrip> strips)
    {
        var uv = type != NjPolyChunk.StripType;
        var user = strips.SelectMany(s => s.Vertices.Skip(2)).Select(v => v.User.Length / 2).DefaultIfEmpty(0).Max();
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((ushort)(strips.Count | user << 14));
        foreach (var strip in strips)
        {
            bw.Write((short)(strip.Flipped ? -strip.Vertices.Count : strip.Vertices.Count));
            for (var v = 0; v < strip.Vertices.Count; v++)
            {
                var vertex = strip.Vertices[v];
                bw.Write(vertex.Index);
                if (uv)
                {
                    bw.Write(vertex.U);
                    bw.Write(vertex.V);
                }
                if (v >= 2)
                {
                    var data = new byte[user * 2];
                    vertex.User.CopyTo(data, 0);
                    bw.Write(data);
                }
            }
        }
        // the size field counts 16-bit words; the chunk (4-byte head + body) ends on a 32-bit word
        while (ms.Length % 4 != 0)
            bw.Write((byte)0);
        return new NjPolyChunk(type, headBits, ms.ToArray());
    }
}

public sealed record NjStripVertex(ushort Index, short U, short V, byte[] User)
{
    public NjStripVertex(ushort index, short u, short v) : this(index, u, v, []) { }
}
