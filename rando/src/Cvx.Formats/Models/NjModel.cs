using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Cvx.Formats.Models;

/// <summary>
/// A Ninja chunk model (<c>NJS_CNK_MODEL</c>): its vertex and polygon lists, centre and radius.
/// </summary>
/// <remarks>
/// In the file the radius is negative: <c>bhCnkBinRealize</c> (binfunc.c) relocates the two
/// list offsets only when it is, then makes it positive. <see cref="Radius"/> is the positive
/// value; it must not be 0.
/// </remarks>
public sealed class NjChunkModel
{
    /// <summary>The vertex list, or null when the file has none (offset -1).</summary>
    public List<NjVertexChunk>? Vertices { get; set; } = [];
    /// <summary>The polygon list, or null when the file has none (offset -1).</summary>
    public List<NjPolyChunk>? Polygons { get; set; } = [];
    public Vector3 Center { get; set; }
    public float Radius { get; set; }
    /// <summary>The 3 bytes after the end of the vertex list (type 0xFF).</summary>
    internal byte[] VertexEnd { get; set; } = [0, 0, 0];
    /// <summary>The head bits of the end of the polygon list.</summary>
    internal byte PolygonEnd { get; set; }

    public int VertexCount => Vertices?.Sum(v => v.Count) ?? 0;
}

/// <summary>
/// A node of a model's object tree (<c>NJS_CNK_OBJECT</c>, 0x34 bytes). Angles are Ninja
/// angles (0x10000 = 360 degrees). <see cref="Child"/> and <see cref="Sibling"/> are indexes
/// into <see cref="NjModel.Objects"/>, -1 for none.
/// </summary>
public sealed class NjObject
{
    public uint EvalFlags { get; set; }
    public NjChunkModel? Model { get; set; }
    public Vector3 Position { get; set; }
    public (int X, int Y, int Z) Angle { get; set; }
    public Vector3 Scale { get; set; } = Vector3.One;
    public int Child { get; set; } = -1;
    public int Sibling { get; set; } = -1;

    /// <summary>Ninja eval flag: the object is not drawn (<c>EasyMultiDrawTreeCnk</c> skips models with it).</summary>
    public const uint EvalHide = 0x8;
    /// <summary>Skipped by the SKIN walk (<c>npSkinConvPreparation</c>, njplus.c).</summary>
    public const uint EvalNoSkin = 0x20000000;
}

/// <summary>
/// A model as the game stores it in MLB form (<c>bhMlbBinRealize</c>, binfunc.c): a header
/// (magic "MDL", status, header size, object count, texture list and object array offsets),
/// then the data the offsets point into, relative to the end of the header: the texture
/// list, the objects (an array: the game walks it with <c>objP++</c>) and their models.
/// </summary>
/// <remarks>
/// Layout of the game's files, which <see cref="ToBytes"/> reproduces byte for byte: the
/// texture list (<c>NJS_TEXLIST</c>, then the <c>NJS_TEXNAME</c> array, then the names),
/// padded to 4; the objects; then for each object with a model, in object order: the model
/// (0x18 bytes, 4-aligned), zeros up to a 64-byte boundary of the model page (the PS2's
/// vertex DMA), the vertex list and the polygon list; finally zeros (see <see cref="EndPadding"/>).
/// Status bit 0x80 marks chunk models (<c>bhCnkBinRealize</c>); others are basic models,
/// which this does not read.
/// </remarks>
public sealed class NjModel
{
    private const int HeaderSize = 16;
    private const int ObjectSize = 0x34;
    private const int ModelSize = 0x18;
    private const int VertexAlignment = 64;

    /// <summary>Byte 3 of the header; 0x80 = chunk models.</summary>
    public byte Status { get; set; } = 0xBC;
    public List<NjObject> Objects { get; } = [];
    /// <summary>The texture names (<c>NJS_TEXNAME.filename</c>), or null for no texture list.</summary>
    public List<string>? Textures { get; set; } = [];
    /// <summary>The header's extra bytes (header size above 16).</summary>
    internal byte[] ExtraHeader { get; set; } = [];
    /// <summary>
    /// For a model read from a file, the zeros after its data: the rooms' models end on a
    /// multiple of 64 and have 64 more, the player's (SYSTEM.AFS) have none. Null: the rooms' way.
    /// </summary>
    internal int? EndPadding { get; set; }

    public bool IsChunk => (Status & 0x80) != 0;

    public static bool IsModel(ReadOnlySpan<byte> data) => data.Length >= HeaderSize && data[0] == 'M' && data[1] == 'D' && data[2] == 'L';

    public static bool IsChunkModel(ReadOnlySpan<byte> data) => IsModel(data) && (data[3] & 0x80) != 0;

    public static NjModel Read(ReadOnlySpan<byte> data)
    {
        if (!IsModel(data))
            throw new InvalidDataException("Not an MLB model (no MDL magic).");
        var model = new NjModel { Status = data[3] };
        if (!model.IsChunk)
            throw new InvalidDataException($"Basic models are not supported (status 0x{data[3]:X2}).");
        var headerSize = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        var count = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        var texOffset = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
        var objOffset = BinaryPrimitives.ReadInt32LittleEndian(data[12..]);
        model.ExtraHeader = data[HeaderSize..headerSize].ToArray();
        var body = data[headerSize..];
        var end = 0;

        if (texOffset == -1)
        {
            model.Textures = null;
        }
        else
        {
            var names = I32(body, texOffset);
            var n = I32(body, texOffset + 4);
            for (var i = 0; i < n; i++)
            {
                var at = names + i * 12;
                if (I32(body, at + 4) != 0 || I32(body, at + 8) != 0)
                    throw new InvalidDataException($"Texture {i} has attributes set.");
                model.Textures!.Add(ReadString(body, I32(body, at)));
                end = Math.Max(end, I32(body, at) + model.Textures[^1].Length + 1);
            }
            end = Math.Max(end, names + n * 12);
        }

        if (objOffset == -1)
        {
            model.EndPadding = data.Length - headerSize - Align(end, 4);
            return model;
        }
        end = Math.Max(end, objOffset + count * ObjectSize);
        var models = new Dictionary<int, NjChunkModel>();
        int Index(int offset)
        {
            if (offset == -1)
                return -1;
            var rel = offset - objOffset;
            if (rel < 0 || rel % ObjectSize != 0 || rel / ObjectSize >= count)
                throw new InvalidDataException($"Object link 0x{offset:X} is not an object of the array.");
            return rel / ObjectSize;
        }
        for (var i = 0; i < count; i++)
        {
            var o = body[(objOffset + i * ObjectSize)..];
            var mdl = I32(o, 4);
            NjChunkModel? m = null;
            if (mdl != -1 && !models.TryGetValue(mdl, out m))
                models[mdl] = m = ReadChunkModel(body, mdl, ref end);
            model.Objects.Add(new NjObject
            {
                EvalFlags = BinaryPrimitives.ReadUInt32LittleEndian(o),
                Model = m,
                Position = ReadVector(o[8..]),
                Angle = (I32(o, 20), I32(o, 24), I32(o, 28)),
                Scale = ReadVector(o[32..]),
                Child = Index(I32(o, 44)),
                Sibling = Index(I32(o, 48)),
            });
        }
        model.EndPadding = data.Length - headerSize - Align(end, 4);
        return model;
    }

    private static NjChunkModel ReadChunkModel(ReadOnlySpan<byte> body, int at, ref int end)
    {
        var vlist = I32(body, at);
        var plist = I32(body, at + 4);
        var r = BinaryPrimitives.ReadSingleLittleEndian(body[(at + 20)..]);
        if (!(r < 0))
            throw new InvalidDataException($"Model at 0x{at:X} has radius {r} (the game relocates only negative ones).");
        var model = new NjChunkModel { Center = ReadVector(body[(at + 8)..]), Radius = -r };
        end = Math.Max(end, at + ModelSize);

        if (vlist == -1)
        {
            model.Vertices = null;
        }
        else
        {
            var p = vlist;
            while (body[p] != 0xFF)
            {
                var words = BinaryPrimitives.ReadUInt16LittleEndian(body[(p + 2)..]);
                model.Vertices!.Add(new NjVertexChunk(body[p], body[p + 1], body.Slice(p + 4, words * 4).ToArray()));
                p += 4 + words * 4;
            }
            model.VertexEnd = body.Slice(p + 1, 3).ToArray();
            end = Math.Max(end, p + 4);
        }

        if (plist == -1)
        {
            model.Polygons = null;
        }
        else
        {
            var p = plist;
            while (body[p] != 0xFF)
            {
                var type = body[p];
                var bits = body[p + 1];
                int length;
                switch (NjPolyChunk.KindOf(type))
                {
                    case NjPolyKind.Null:
                    case NjPolyKind.Bits:
                        length = 0;
                        p += 2;
                        break;
                    case NjPolyKind.Tiny:
                        length = 2;
                        p += 2;
                        break;
                    case NjPolyKind.Material:
                    case NjPolyKind.Volume:
                    case NjPolyKind.Strip:
                        length = BinaryPrimitives.ReadUInt16LittleEndian(body[(p + 2)..]) * 2;
                        p += 4;
                        break;
                    default:
                        throw new InvalidDataException($"Unknown polygon chunk type {type} at 0x{p:X}.");
                }
                model.Polygons!.Add(new NjPolyChunk(type, bits, body.Slice(p, length).ToArray()));
                p += length;
            }
            model.PolygonEnd = body[p + 1];
            end = Math.Max(end, p + 2);
        }
        return model;
    }

    /// <summary>
    /// Writes the model. <paramref name="pageOffset"/> is where it will start in its model
    /// page, for the vertex lists' 64-byte alignment.
    /// </summary>
    public byte[] ToBytes(int pageOffset = 0)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        var headerSize = HeaderSize + ExtraHeader.Length;
        bw.Write("MDL"u8);
        bw.Write(Status);
        bw.Write((ushort)headerSize);
        bw.Write((ushort)Objects.Count);
        bw.Write(0);    // texture list, objects: filled in below
        bw.Write(0);
        bw.Write(ExtraHeader);
        int Rel() => (int)ms.Position - headerSize;

        var texOffset = -1;
        if (Textures != null)
        {
            texOffset = Rel();
            var names = Textures.Select(t => Encoding.ASCII.GetBytes(t + "\0")).ToArray();
            bw.Write(texOffset + 8);
            bw.Write(Textures.Count);
            var name = texOffset + 8 + 12 * Textures.Count;
            foreach (var n in names)
            {
                bw.Write(name);
                bw.Write(0L);
                name += n.Length;
            }
            foreach (var n in names)
                bw.Write(n);
            Pad(bw, 4);
        }

        var objOffset = Objects.Count == 0 ? -1 : Rel();
        var objectsAt = ms.Position;
        bw.Write(new byte[Objects.Count * ObjectSize]);

        var modelOffsets = new Dictionary<NjChunkModel, int>(ReferenceEqualityComparer.Instance);
        foreach (var obj in Objects)
        {
            if (obj.Model == null || modelOffsets.ContainsKey(obj.Model))
                continue;
            var m = obj.Model;
            Pad(bw, 4);
            modelOffsets[m] = Rel();
            var modelAt = ms.Position;
            bw.Write(new byte[ModelSize]);

            var vlist = -1;
            if (m.Vertices != null)
            {
                while ((pageOffset + ms.Position) % VertexAlignment != 0)
                    bw.Write((byte)0);
                vlist = Rel();
                foreach (var v in m.Vertices)
                {
                    bw.Write(v.Type);
                    bw.Write(v.HeadBits);
                    bw.Write((ushort)(v.Body.Length / 4));
                    bw.Write(v.Body);
                }
                bw.Write((byte)0xFF);
                bw.Write(m.VertexEnd);
            }
            var plist = -1;
            if (m.Polygons != null)
            {
                plist = Rel();
                foreach (var c in m.Polygons)
                {
                    bw.Write(c.Type);
                    bw.Write(c.HeadBits);
                    if (c.Kind is NjPolyKind.Material or NjPolyKind.Volume or NjPolyKind.Strip)
                        bw.Write((ushort)(c.Body.Length / 2));
                    bw.Write(c.Body);
                }
                bw.Write((byte)0xFF);
                bw.Write(m.PolygonEnd);
            }

            var end = ms.Position;
            ms.Position = modelAt;
            bw.Write(vlist);
            bw.Write(plist);
            WriteVector(bw, m.Center);
            bw.Write(-m.Radius);
            ms.Position = end;
        }

        // Objects.
        var after = ms.Position;
        ms.Position = objectsAt;
        foreach (var obj in Objects)
        {
            bw.Write(obj.EvalFlags);
            bw.Write(obj.Model == null ? -1 : modelOffsets[obj.Model]);
            WriteVector(bw, obj.Position);
            bw.Write(obj.Angle.X);
            bw.Write(obj.Angle.Y);
            bw.Write(obj.Angle.Z);
            WriteVector(bw, obj.Scale);
            bw.Write(obj.Child == -1 ? -1 : objOffset + obj.Child * ObjectSize);
            bw.Write(obj.Sibling == -1 ? -1 : objOffset + obj.Sibling * ObjectSize);
        }
        ms.Position = 8;
        bw.Write(texOffset);
        bw.Write(objOffset);
        ms.Position = after;
        if (EndPadding is { } padding)
        {
            Pad(bw, 4);
            bw.Write(new byte[padding]);
        }
        else
        {
            Pad(bw, 64);
            bw.Write(new byte[64]);
        }
        bw.Flush();
        return ms.ToArray();
    }

    /// <summary>Each object's parent index (-1 for roots), from the child and sibling links.</summary>
    public int[] Parents()
    {
        var parents = Enumerable.Repeat(-1, Objects.Count).ToArray();
        for (var i = 0; i < Objects.Count; i++)
        {
            for (var c = Objects[i].Child; c != -1; c = Objects[c].Sibling)
                parents[c] = i;
        }
        return parents;
    }

    /// <summary>The objects in tree order (depth first, child before sibling), as the game walks them.</summary>
    public IEnumerable<int> TreeOrder()
    {
        if (Objects.Count == 0)
            yield break;
        var stack = new Stack<int>();
        stack.Push(0);
        while (stack.Count != 0)
        {
            var i = stack.Pop();
            yield return i;
            if (Objects[i].Sibling != -1)
                stack.Push(Objects[i].Sibling);
            if (Objects[i].Child != -1)
                stack.Push(Objects[i].Child);
        }
    }

    /// <summary>
    /// Each object's position in the model's space in the bind pose (the objects' own
    /// translations added down the tree; rotations and scales are taken as identity, which
    /// is what the game's own NPC skeletons have).
    /// </summary>
    public Vector3[] BindPositions()
    {
        var result = new Vector3[Objects.Count];
        var parents = Parents();
        foreach (var i in TreeOrder())
            result[i] = (parents[i] == -1 ? Vector3.Zero : result[parents[i]]) + Objects[i].Position;
        return result;
    }

    private static void Pad(BinaryWriter bw, int alignment)
    {
        bw.Flush();
        while (bw.BaseStream.Position % alignment != 0)
            bw.Write((byte)0);
    }

    private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;

    private static int I32(ReadOnlySpan<byte> s, int at) => BinaryPrimitives.ReadInt32LittleEndian(s[at..]);

    private static string ReadString(ReadOnlySpan<byte> s, int at)
    {
        var end = s[at..].IndexOf((byte)0);
        return Encoding.ASCII.GetString(s.Slice(at, end));
    }

    private static Vector3 ReadVector(ReadOnlySpan<byte> s) => new(
        BinaryPrimitives.ReadSingleLittleEndian(s),
        BinaryPrimitives.ReadSingleLittleEndian(s[4..]),
        BinaryPrimitives.ReadSingleLittleEndian(s[8..]));

    private static void WriteVector(BinaryWriter bw, Vector3 v)
    {
        bw.Write(v.X);
        bw.Write(v.Y);
        bw.Write(v.Z);
    }
}
