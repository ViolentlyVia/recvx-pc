// Ported from BioRand's biohazard-utils (CvModelList.cs, CvMotionList.cs, CvTextureList.cs,
// CvMsgList.cs, CvCameraList.cs, ScdProcedureList.cs), MIT, Copyright (c) 2022-2026 Ted John.

using System.Runtime.InteropServices;

namespace Cvx.Formats.Rdx;

/// <summary>Camera cuts (<c>CUT_WORK</c>), kept as raw bytes.</summary>
public readonly record struct RdxCameraList(int Count, ReadOnlyMemory<byte> Data);

/// <summary>Room messages; the offsets in it are relative to its start.</summary>
public readonly struct RdxMessageList(ReadOnlyMemory<byte> data)
{
    public ReadOnlyMemory<byte> Data { get; } = data;

    public int Count => Data.Length < 4 ? 0 : MemoryMarshal.Read<int>(Data.Span);
}

/// <summary>
/// A list of pages addressed by absolute file offsets: a table of offsets (0 = empty page)
/// followed by the pages. Used for models (<c>hed01</c>) and motions (<c>hed02</c>).
/// </summary>
public readonly struct RdxPageList(int baseOffset, ReadOnlyMemory<byte> data)
{
    public int BaseOffset { get; } = baseOffset;
    public ReadOnlyMemory<byte> Data { get; } = data;

    public int PageCount
    {
        get
        {
            var offsets = MemoryMarshal.Cast<byte, int>(Data.Span);
            foreach (var offset in offsets)
            {
                if (offset != 0)
                    return (offset - BaseOffset) / 4;
            }
            return 0;
        }
    }

    public ReadOnlySpan<int> Offsets => Data.GetSafeSpan<int>(0, PageCount);

    public ReadOnlyMemory<byte>[] Pages
    {
        get
        {
            var offsets = Offsets.ToArray();
            var ends = offsets.Where(x => x != 0).Append(BaseOffset + Data.Length).ToArray();
            var pages = new ReadOnlyMemory<byte>[offsets.Length];
            for (var i = 0; i < offsets.Length; i++)
            {
                var offset = offsets[i];
                if (offset == 0)
                    continue;
                var end = ends.First(x => x > offset);
                pages[i] = Data.Slice(offset - BaseOffset, end - offset);
            }
            return pages;
        }
    }

    /// <summary>The same list moved to a new file offset.</summary>
    public RdxPageList WithNewBaseOffset(int baseOffset)
    {
        var result = Data.ToArray();
        var offsets = MemoryMarshal.Cast<byte, int>(result.AsSpan(0, PageCount * 4));
        for (var i = 0; i < offsets.Length; i++)
        {
            if (offsets[i] != 0)
                offsets[i] = baseOffset + (offsets[i] - BaseOffset);
        }
        return new RdxPageList(baseOffset, result);
    }

    /// <summary>Builds a list from pages; an empty page gets offset 0.</summary>
    public static RdxPageList FromPages(int baseOffset, IReadOnlyList<ReadOnlyMemory<byte>> pages)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        var offset = baseOffset + pages.Count * 4;
        foreach (var page in pages)
        {
            if (page.Length == 0)
            {
                bw.Write(0);
            }
            else
            {
                bw.Write(offset);
                offset += page.Length;
            }
        }
        foreach (var page in pages)
            bw.Write(page.Span);
        bw.Flush();
        return new RdxPageList(baseOffset, ms.ToArray());
    }
}

/// <summary>Textures (<c>hed04</c>): a count, absolute offsets to each group, then the groups.</summary>
public readonly struct RdxTextureList(int baseOffset, ReadOnlyMemory<byte> data)
{
    public int BaseOffset { get; } = baseOffset;
    public ReadOnlyMemory<byte> Data { get; } = data;

    public int Count => Data.Length < 4 ? 0 : Data.GetSafeSpan<int>(0, 1)[0];
    public ReadOnlySpan<int> Offsets => Data.GetSafeSpan<int>(4, Count);
    public int HeaderSize => Count == 0 ? Data.Length : Offsets[0] - BaseOffset;

    public ReadOnlyMemory<byte>[] Groups
    {
        get
        {
            var offsets = Offsets.ToArray();
            var result = new ReadOnlyMemory<byte>[offsets.Length];
            for (var i = 0; i < offsets.Length; i++)
            {
                var start = offsets[i] - BaseOffset;
                var end = i < offsets.Length - 1 ? offsets[i + 1] - BaseOffset : Data.Length;
                result[i] = Data[start..end];
            }
            return result;
        }
    }

    public RdxTextureList WithNewBaseOffset(int baseOffset)
    {
        if (Count == 0)
            return new RdxTextureList(baseOffset, Data);
        var result = Data.ToArray();
        var offsets = MemoryMarshal.Cast<byte, int>(result.AsSpan(4, Count * 4));
        for (var i = 0; i < offsets.Length; i++)
            offsets[i] = baseOffset + (offsets[i] - BaseOffset);
        return new RdxTextureList(baseOffset, result);
    }
}

/// <summary>Event scripts (<c>evtp</c>): a table of procedure offsets, relative to its start.</summary>
public readonly struct RdxScript(ReadOnlyMemory<byte> data)
{
    public ReadOnlyMemory<byte> Data { get; } = data;

    public int Count => Data.Length < 4 ? 0 : GetOffset(0) / 4;

    public int GetOffset(int index) => MemoryMarshal.Read<int>(Data.Span[(index * 4)..]);

    /// <summary>Procedure <paramref name="index"/> (0 runs at room start, 1 every frame).</summary>
    public ReadOnlyMemory<byte> this[int index]
    {
        get
        {
            var offset = GetOffset(index);
            var end = index == Count - 1 ? Data.Length : GetOffset(index + 1);
            return Data[offset..end];
        }
    }

    public static RdxScript FromProcedures(IReadOnlyList<ReadOnlyMemory<byte>> procedures)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        var offset = procedures.Count * 4;
        foreach (var p in procedures)
        {
            bw.Write(offset);
            offset += p.Length;
        }
        foreach (var p in procedures)
            bw.Write(p.Span);
        bw.Flush();
        return new RdxScript(ms.ToArray());
    }
}
