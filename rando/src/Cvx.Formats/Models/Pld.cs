// Ported from BioRand's biohazard-utils (Model/ModelFile.cs, Model/PldFile.cs, Model/Emr.cs,
// Model/Md1.cs, TimFile.cs), MIT, Copyright (c) 2022-2026 Ted John. Reads only what the
// character converter needs: the skeleton's rest pose, the parts and the texture.

using System.Buffers.Binary;
using System.Numerics;

namespace Cvx.Formats.Models;

/// <summary>
/// A Resident Evil 2 player model (PLD): a directory of four chunks (animations, armature,
/// mesh, texture). BioRand's community converts characters of other games into this form
/// (<c>data/re2/pld0</c>, <c>pld1</c> in its datapacks).
/// </summary>
/// <remarks>
/// Coordinates are the PS1's: Y points down. The characters face +X with their right side
/// toward -Z, arms hanging down, about 3,000 units tall.
/// </remarks>
public sealed class Pld
{
    public required Emr Armature { get; init; }
    public required Md1 Mesh { get; init; }
    public required PsxTim Texture { get; init; }

    public static Pld Read(ReadOnlySpan<byte> data)
    {
        var directory = BinaryPrimitives.ReadInt32LittleEndian(data);
        var count = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        if (count != 4 || directory + count * 4 != data.Length)
            throw new InvalidDataException($"Not an RE2 PLD (directory at 0x{directory:X}, {count} chunks).");
        var offsets = new int[count + 1];
        for (var i = 0; i < count; i++)
            offsets[i] = BinaryPrimitives.ReadInt32LittleEndian(data[(directory + i * 4)..]);
        offsets[count] = directory;
        return new Pld
        {
            Armature = Emr.Read(data[offsets[1]..offsets[2]]),
            Mesh = Md1.Read(data[offsets[2]..offsets[3]]),
            Texture = PsxTim.Read(data[offsets[3]..offsets[4]]),
        };
    }
}

/// <summary>
/// An RE2 skeleton (EMR): each part's offset from its parent in the rest pose and the
/// parts' children. Part 0 is the root.
/// </summary>
public sealed class Emr
{
    public required Vector3[] Offsets { get; init; }
    public required int[][] Children { get; init; }

    public int Count => Offsets.Length;

    public static Emr Read(ReadOnlySpan<byte> data)
    {
        var armature = BinaryPrimitives.ReadUInt16LittleEndian(data);
        var parts = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        var offsets = new Vector3[parts];
        var children = new int[parts][];
        for (var i = 0; i < parts; i++)
        {
            var at = 8 + i * 6;
            offsets[i] = new Vector3(
                BinaryPrimitives.ReadInt16LittleEndian(data[at..]),
                BinaryPrimitives.ReadInt16LittleEndian(data[(at + 2)..]),
                BinaryPrimitives.ReadInt16LittleEndian(data[(at + 4)..]));
            var n = BinaryPrimitives.ReadInt16LittleEndian(data[(armature + i * 4)..]);
            var list = BinaryPrimitives.ReadInt16LittleEndian(data[(armature + i * 4 + 2)..]);
            children[i] = data.Slice(armature + list, n).ToArray().Select(b => (int)b).ToArray();
        }
        return new Emr { Offsets = offsets, Children = children };
    }

    public int[] Parents()
    {
        var parents = Enumerable.Repeat(-1, Count).ToArray();
        for (var i = 0; i < Count; i++)
        {
            foreach (var c in Children[i])
                parents[c] = i;
        }
        return parents;
    }

    /// <summary>Each part's position in the rest pose (offsets added down the tree).</summary>
    public Vector3[] RestPositions()
    {
        var result = new Vector3[Count];
        var parents = Parents();
        var order = new List<int> { 0 };
        for (var k = 0; k < order.Count; k++)
            order.AddRange(Children[order[k]]);
        foreach (var i in order)
            result[i] = (parents[i] == -1 ? Vector3.Zero : result[parents[i]]) + Offsets[i];
        return result;
    }
}

/// <summary>
/// An RE2 mesh (MD1): rigid parts, each a pair of objects (triangles, quads) sharing its
/// positions and normals, in the part's own space around its joint.
/// </summary>
public sealed class Md1
{
    public required IReadOnlyList<Md1Part> Parts { get; init; }

    public static Md1 Read(ReadOnlySpan<byte> data)
    {
        var objects = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
        var body = data[12..];
        var parts = new List<Md1Part>();
        for (var i = 0; i + 1 < objects; i += 2)
        {
            var tri = Descriptor(body, i);
            var quad = Descriptor(body, i + 1);
            var positions = Vectors(body, tri.Vertices, tri.VertexCount);
            var normals = Vectors(body, tri.Normals, tri.NormalCount);
            var polygons = new List<Md1Polygon>();
            for (var k = 0; k < tri.Count; k++)
            {
                var p = body[(tri.Primitives + k * 12)..];
                var t = body[(tri.Textures + k * 12)..];
                polygons.Add(new Md1Polygon(
                    [U16(p, 2), U16(p, 6), U16(p, 10)], [U16(p, 0), U16(p, 4), U16(p, 8)],
                    [(t[0], t[1]), (t[4], t[5]), (t[8], t[9])], U16(t, 6) & 0x0F, U16(t, 2)));
            }
            for (var k = 0; k < quad.Count; k++)
            {
                var p = body[(quad.Primitives + k * 16)..];
                var t = body[(quad.Textures + k * 16)..];
                polygons.Add(new Md1Polygon(
                    [U16(p, 2), U16(p, 6), U16(p, 10), U16(p, 14)], [U16(p, 0), U16(p, 4), U16(p, 8), U16(p, 12)],
                    [(t[0], t[1]), (t[4], t[5]), (t[8], t[9]), (t[12], t[13])], U16(t, 6) & 0x0F, U16(t, 2)));
            }
            parts.Add(new Md1Part(positions, normals, polygons));
        }
        return new Md1 { Parts = parts };
    }

    private readonly record struct ObjectDescriptor(int Vertices, int VertexCount, int Normals, int NormalCount, int Primitives, int Count, int Textures);

    private static ObjectDescriptor Descriptor(ReadOnlySpan<byte> body, int index)
    {
        var d = body[(index * 28)..];
        return new ObjectDescriptor(I32(d, 0), I32(d, 4), I32(d, 8), I32(d, 12), I32(d, 16), I32(d, 20), I32(d, 24));
    }

    private static Vector3[] Vectors(ReadOnlySpan<byte> body, int offset, int count)
    {
        var result = new Vector3[count];
        for (var i = 0; i < count; i++)
        {
            var v = body[(offset + i * 8)..];
            result[i] = new Vector3(BinaryPrimitives.ReadInt16LittleEndian(v), BinaryPrimitives.ReadInt16LittleEndian(v[2..]), BinaryPrimitives.ReadInt16LittleEndian(v[4..]));
        }
        return result;
    }

    private static int I32(ReadOnlySpan<byte> s, int at) => BinaryPrimitives.ReadInt32LittleEndian(s[at..]);
    private static int U16(ReadOnlySpan<byte> s, int at) => BinaryPrimitives.ReadUInt16LittleEndian(s[at..]);
}

public sealed record Md1Part(Vector3[] Positions, Vector3[] Normals, IReadOnlyList<Md1Polygon> Polygons);

/// <summary>
/// A triangle or quad (quads in the PS1's order: 0, 1, 2 and 1, 3, 2 are its triangles).
/// UVs are pixels within texture page <see cref="Page"/> (128 pixels wide at 8 bits, 256 at
/// 4 bits); <see cref="Clut"/> is the PS1 CLUT id (row = (id >> 6) - the texture's CLUT y).
/// </summary>
public sealed record Md1Polygon(int[] Vertices, int[] Normals, (byte U, byte V)[] Uvs, int Page, int Clut);

/// <summary>A PS1 TIM image with its CLUTs (4 or 8 bits per pixel), as RE2's PLDs carry it.</summary>
public sealed class PsxTim
{
    public required int BitsPerPixel { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    /// <summary>The pixels' palette indexes, one byte each.</summary>
    public required byte[] Pixels { get; init; }
    /// <summary>The CLUTs: 16-bit PS1 colours, <see cref="ClutColors"/> each.</summary>
    public required ushort[][] Cluts { get; init; }
    public required int ClutColors { get; init; }
    /// <summary>The CLUTs' VRAM y (a CLUT id's row is <c>(id >> 6) - ClutY</c>).</summary>
    public required int ClutY { get; init; }

    public static PsxTim Read(ReadOnlySpan<byte> data)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(data) != 0x10)
            throw new InvalidDataException("Not a TIM image.");
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        var bpp = (flags & 3) switch { 0 => 4, 1 => 8, _ => throw new NotSupportedException($"TIM pixel mode {flags & 3} is not supported.") };
        if ((flags & 8) == 0)
            throw new NotSupportedException("TIM without a CLUT.");
        var p = 8;
        var clutLength = BinaryPrimitives.ReadInt32LittleEndian(data[p..]);
        var clutY = BinaryPrimitives.ReadUInt16LittleEndian(data[(p + 6)..]);
        var colors = BinaryPrimitives.ReadUInt16LittleEndian(data[(p + 8)..]);
        var count = BinaryPrimitives.ReadUInt16LittleEndian(data[(p + 10)..]);
        var cluts = new ushort[count][];
        for (var c = 0; c < count; c++)
        {
            cluts[c] = new ushort[colors];
            for (var i = 0; i < colors; i++)
                cluts[c][i] = BinaryPrimitives.ReadUInt16LittleEndian(data[(p + 12 + (c * colors + i) * 2)..]);
        }
        p += clutLength;
        // the image block's length is not always right in BioRand's files: use the size
        var halfwords = BinaryPrimitives.ReadUInt16LittleEndian(data[(p + 8)..]);
        var height = BinaryPrimitives.ReadUInt16LittleEndian(data[(p + 10)..]);
        var width = halfwords * 16 / bpp;
        var image = data.Slice(p + 12, Math.Min(halfwords * 2 * height, data.Length - p - 12));
        var pixels = new byte[width * height];
        for (var i = 0; i < pixels.Length && i * bpp / 8 < image.Length; i++)
            pixels[i] = bpp == 8 ? image[i] : (byte)((image[i / 2] >> (i % 2 * 4)) & 0xF);
        return new PsxTim { BitsPerPixel = bpp, Width = width, Height = height, Pixels = pixels, Cluts = cluts, ClutColors = colors, ClutY = clutY };
    }

    /// <summary>A pixel as RGBA (PS1 colour 0 is transparent; the STP bit is ignored).</summary>
    public uint Rgba(int x, int y, int clut)
    {
        var c = Cluts[Math.Clamp(clut, 0, Cluts.Length - 1)][Pixels[y * Width + x]];
        if (c == 0)
            return 0;
        uint r = (uint)(c & 0x1F) * 255 / 31, g = (uint)((c >> 5) & 0x1F) * 255 / 31, b = (uint)((c >> 10) & 0x1F) * 255 / 31;
        return r | g << 8 | b << 16 | 0xFFu << 24;
    }

    /// <summary>The width of a texture page in pixels (64 VRAM halfwords).</summary>
    public int PageWidth => 64 * 16 / BitsPerPixel;
}
