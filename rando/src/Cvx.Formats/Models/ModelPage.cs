using System.Buffers.Binary;

namespace Cvx.Formats.Models;

public enum ModelPageBlockKind { Empty, Model, Skin, Mask }

/// <summary>
/// One block of a model page: its bytes (as many as its declared size), and for a chunk model
/// block the model read from them.
/// </summary>
public sealed class ModelPageBlock
{
    public ModelPageBlockKind Kind { get; init; }
    /// <summary>The block's bytes as read (for an empty entry, none).</summary>
    public byte[] Data { get; init; } = [];
    /// <summary>For a chunk model block, the model; the page writes it instead of <see cref="Data"/>.</summary>
    public NjModel? Model { get; set; }

    public static ModelPageBlock FromModel(NjModel model) => new() { Kind = ModelPageBlockKind.Model, Model = model };
    public static ModelPageBlock FromSkin(SkinData skin) => new() { Kind = ModelPageBlockKind.Skin, Data = skin.ToBytes() };
}

/// <summary>
/// A model page as <c>bhSetEneMdl</c> (room.c) and the player loader (dread.c) read it: blocks
/// of <c>[u32 size][size bytes]</c> until a size of -1; a size of 0 is an empty entry. A block
/// is a SKIN block (vertex weights for the next model, <c>npSkinConvert</c>), a MASK block
/// (the face animation, <c>bhInitMask</c>, face.c) or an MLB model (<see cref="NjModel"/>).
/// Each model with textures takes the next texture entry of its enemy type.
/// </summary>
public sealed class ModelPage
{
    public const uint SkinMagic = 0x4E494B53;   // "SKIN"
    public const uint MaskMagic = 0x4B53414D;   // "MASK"

    public List<ModelPageBlock> Blocks { get; } = [];
    /// <summary>Bytes after the end marker.</summary>
    public byte[] Tail { get; set; } = [];

    /// <summary>The chunk models (basic models, which some player pages have, are kept as bytes).</summary>
    public IEnumerable<NjModel> Models => Blocks.Select(b => b.Model).OfType<NjModel>();

    public static ModelPage Read(ReadOnlySpan<byte> data)
    {
        var page = new ModelPage();
        var p = 0;
        while (true)
        {
            if (p + 4 > data.Length)
                throw new InvalidDataException("Model page has no end marker.");
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data[p..]);
            p += 4;
            if (size == 0xFFFFFFFF)
                break;
            if (size == 0)
            {
                page.Blocks.Add(new ModelPageBlock { Kind = ModelPageBlockKind.Empty });
                continue;
            }
            if (p + size > data.Length)
                throw new InvalidDataException($"Model page block at 0x{p - 4:X} runs past the end.");
            var block = data.Slice(p, (int)size);
            var magic = BinaryPrimitives.ReadUInt32LittleEndian(block);
            var kind = magic switch
            {
                SkinMagic => ModelPageBlockKind.Skin,
                MaskMagic => ModelPageBlockKind.Mask,
                _ => ModelPageBlockKind.Model,
            };
            page.Blocks.Add(new ModelPageBlock
            {
                Kind = kind,
                Data = block.ToArray(),
                Model = kind == ModelPageBlockKind.Model && NjModel.IsChunkModel(block) ? NjModel.Read(block) : null,
            });
            p += (int)size;
        }
        page.Tail = data[p..].ToArray();
        return page;
    }

    /// <summary>Writes the page; model blocks are written from their <see cref="ModelPageBlock.Model"/>.</summary>
    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        foreach (var block in Blocks)
        {
            if (block.Kind == ModelPageBlockKind.Empty)
            {
                bw.Write(0);
                continue;
            }
            var data = block.Model != null ? block.Model.ToBytes((int)ms.Position + 4) : block.Data;
            bw.Write(data.Length);
            bw.Write(data);
        }
        bw.Write(-1);
        bw.Write(Tail);
        bw.Flush();
        return ms.ToArray();
    }
}

/// <summary>
/// A SKIN block's words (after the magic), as <c>npSkinConvPreparation</c> and
/// <c>npSkinConvSub</c> (njplus.c) walk them: one entry per object in tree order (objects with
/// eval flag 0x20000000 have none). An entry is a flag word; flag 0 ends it there. Flag bit 2
/// (value 2): the object holds skinned mesh number <c>mesh</c>, and the entry goes on with the
/// mesh number and a count, then that many words, one per vertex of the mesh, whose top byte
/// is the object (tree order) that moves the vertex. Otherwise (flag 1) the entry has one more
/// word: the mesh whose vertices this object moves.
/// </summary>
public sealed class SkinData
{
    public int[] Words { get; }

    public SkinData(int[] words) => Words = words;

    public static SkinData Read(ReadOnlySpan<byte> block)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(block) != ModelPage.SkinMagic)
            throw new InvalidDataException("Not a SKIN block.");
        var words = new int[(block.Length - 4) / 4];
        for (var i = 0; i < words.Length; i++)
            words[i] = BinaryPrimitives.ReadInt32LittleEndian(block[(4 + i * 4)..]);
        return new SkinData(words);
    }

    public byte[] ToBytes()
    {
        var bytes = new byte[4 + Words.Length * 4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, ModelPage.SkinMagic);
        for (var i = 0; i < Words.Length; i++)
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4 + i * 4), Words[i]);
        return bytes;
    }

    /// <summary>One object's SKIN entry.</summary>
    /// <param name="Owners">For a mesh holder, the object (tree order) moving each vertex of the mesh.</param>
    /// <param name="Low">For a mesh holder, the low 24 bits of each vertex word.</param>
    public sealed record Entry(int Object, int Flags, int Mesh, int[] Owners, int[] Low);

    /// <summary>The entries, matched to the model's objects (by index, not tree position).</summary>
    public IReadOnlyList<Entry> Entries(NjModel model)
    {
        var result = new List<Entry>();
        var p = 0;
        foreach (var i in model.TreeOrder())
        {
            if ((model.Objects[i].EvalFlags & NjObject.EvalNoSkin) != 0)
                continue;
            if (p >= Words.Length)
                break;
            var flags = Words[p++];
            if (flags == 0)
                continue;
            var mesh = Words[p++];
            if ((flags & 2) != 0)
            {
                var n = Words[p++];
                var words = Words.AsSpan(p, n);
                result.Add(new Entry(i, flags, mesh, words.ToArray().Select(w => (int)((uint)w >> 24)).ToArray(),
                    words.ToArray().Select(w => w & 0xFFFFFF).ToArray()));
                p += n;
            }
            else
            {
                result.Add(new Entry(i, flags, mesh, [], []));
            }
        }
        return result;
    }
}
