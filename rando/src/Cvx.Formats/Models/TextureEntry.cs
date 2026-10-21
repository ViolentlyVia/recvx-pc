using System.Buffers.Binary;

namespace Cvx.Formats.Models;

/// <summary>
/// One record of a texture entry: a 32-byte head (code, data size, 24 bytes of filler) and
/// the data.
/// </summary>
public sealed record TextureRecord(uint Code, byte[] Filler, byte[] Data)
{
    public const uint Tim2Code = 0x324D4954;     // "TIM2"
    public const uint PaletteCode = 0x00494C50;  // "PLI\0"
    public const uint EndCode = 0xFFFFFFFF;

    public static readonly byte[] DefaultFiller = Enumerable.Range(0, 24).Select(i => (byte)(i % 2 == 0 ? 0x43 : 0x44)).ToArray();
}

/// <summary>
/// A room texture entry as <c>bhSetMemPvpTexture</c> (ps2_texture.c) reads it: records until
/// code -1 (or the end of the entry). A TIM2 record is one texture (<see cref="Tim2Picture"/>); it fills the next name
/// of the model's texture list. A PLI record lists palettes for the TIM2 records after it
/// (each then gives one texture per palette); the game's character textures have none.
/// </summary>
public sealed class TextureEntry
{
    private const int HeadSize = 32;

    public List<TextureRecord> Records { get; } = [];
    /// <summary>Bytes after the end record.</summary>
    public byte[] Tail { get; set; } = [];

    public IEnumerable<Tim2Picture> Pictures => Records.Where(r => r.Code == TextureRecord.Tim2Code).Select(r => new Tim2Picture(r.Data));

    public static TextureEntry Read(ReadOnlySpan<byte> data)
    {
        var entry = new TextureEntry();
        var p = 0;
        // some entries stop at their last picture, without an end record (or with part of
        // one: the last entry of RM_5680 is cut off by the end of the room file)
        while (p + HeadSize <= data.Length)
        {
            var code = BinaryPrimitives.ReadUInt32LittleEndian(data[p..]);
            var size = BinaryPrimitives.ReadInt32LittleEndian(data[(p + 4)..]);
            if (size < 0 || p + HeadSize + size > data.Length)
                throw new InvalidDataException($"Texture record at 0x{p:X} runs past the end.");
            entry.Records.Add(new TextureRecord(code, data.Slice(p + 8, 24).ToArray(), data.Slice(p + HeadSize, size).ToArray()));
            p += HeadSize + size;
            if (code == TextureRecord.EndCode || (code != TextureRecord.Tim2Code && code != TextureRecord.PaletteCode))
                break;
        }
        entry.Tail = data[p..].ToArray();
        return entry;
    }

    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        foreach (var r in Records)
        {
            bw.Write(r.Code);
            bw.Write(r.Data.Length);
            bw.Write(r.Filler);
            bw.Write(r.Data);
        }
        bw.Write(Tail);
        bw.Flush();
        return ms.ToArray();
    }

    /// <summary>An entry of these pictures, ended the way the game's are.</summary>
    public static TextureEntry FromPictures(IEnumerable<Tim2Picture> pictures)
    {
        var entry = new TextureEntry();
        foreach (var picture in pictures)
            entry.Records.Add(new TextureRecord(TextureRecord.Tim2Code, TextureRecord.DefaultFiller, picture.Data));
        entry.Records.Add(new TextureRecord(TextureRecord.EndCode, new byte[24], []));
        return entry;
    }
}

/// <summary>
/// A TIM2 picture as the game stores it (<c>TIM2_PICTUREHEADER_EX</c>, types.h, 0xD0 bytes,
/// padded to 0x100): the TIM2 file head with the game's global index (<c>Gindex</c>) in its
/// reserved bytes, then the TIM2 picture header at 0x80, then the image and the palette.
/// The game's characters use 8-bit images (image type 5) with a 256-colour RGBA palette in
/// the GS's CSM1 order (entries 8-15 and 16-23 of each 32 swapped, <c>ClutCopy256</c>),
/// alpha 0-0x80.
/// </summary>
public sealed class Tim2Picture
{
    private const int DataStart = 0x100;

    public byte[] Data { get; }

    public Tim2Picture(byte[] data)
    {
        if (data.Length < DataStart || BinaryPrimitives.ReadUInt32LittleEndian(data) != TextureRecord.Tim2Code)
            throw new InvalidDataException("Not a TIM2 picture.");
        Data = data;
    }

    public uint GlobalIndex => U32(0x08);
    public int ClutSize => (int)U32(0x84);
    public int ImageSize => (int)U32(0x88);
    public int ClutColors => U16(0x8E);
    public int PictureFormat => Data[0x90];
    public int ImageType => Data[0x93];
    public int Width => U16(0x94);
    public int Height => U16(0x96);

    public ReadOnlySpan<byte> Image => Data.AsSpan(DataStart, ImageSize);

    /// <summary>The palette in index order, as RGBA with the PS2's alpha (0-0x80).</summary>
    public uint[] Palette()
    {
        if (ImageType != 5)
            throw new NotSupportedException($"TIM2 image type {ImageType} is not supported.");
        var stored = Data.AsSpan(DataStart + ImageSize, ClutSize);
        var result = new uint[256];
        for (var i = 0; i < 256; i++)
            result[i] = BinaryPrimitives.ReadUInt32LittleEndian(stored[(Csm1(i) * 4)..]);
        return result;
    }

    /// <summary>The pixels as RGBA bytes (alpha scaled to 0-255).</summary>
    public byte[] ToRgba()
    {
        var palette = Palette();
        var rgba = new byte[Width * Height * 4];
        var image = Image;
        for (var i = 0; i < Width * Height; i++)
        {
            var c = palette[image[i]];
            rgba[i * 4] = (byte)c;
            rgba[i * 4 + 1] = (byte)(c >> 8);
            rgba[i * 4 + 2] = (byte)(c >> 16);
            rgba[i * 4 + 3] = (byte)Math.Min(255, (c >> 24) * 255 / 0x80);
        }
        return rgba;
    }

    /// <summary>
    /// An 8-bit picture made from <paramref name="template"/>'s head (one of the game's
    /// character textures of the same size): new global index, pixels and palette.
    /// </summary>
    /// <param name="palette">256 RGBA colours in index order, alpha 0-0x80.</param>
    public static Tim2Picture Create(Tim2Picture template, uint globalIndex, int width, int height, ReadOnlySpan<byte> pixels, IReadOnlyList<uint> palette)
    {
        if (template.ImageType != 5 || template.Width != width || template.Height != height)
            throw new ArgumentException("The template must be an 8-bit picture of the same size.", nameof(template));
        if (pixels.Length != width * height || palette.Count != 256)
            throw new ArgumentException("Need width x height pixels and 256 colours.");
        var data = template.Data.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x08), globalIndex);
        pixels.CopyTo(data.AsSpan(DataStart));
        for (var i = 0; i < 256; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(DataStart + width * height + Csm1(i) * 4), palette[i]);
        return new Tim2Picture(data);
    }

    /// <summary>Where palette entry <paramref name="i"/> is stored (CSM1: bits 3 and 4 swapped).</summary>
    public static int Csm1(int i) => (i & ~0x18) | ((i & 0x08) << 1) | ((i & 0x10) >> 1);

    private uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(at));
    private int U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan(at));
}
