using System.Buffers.Binary;

namespace Cvx.Formats;

/// <summary>
/// Read access to an AFS archive (such as RDX_LNK.AFS) without loading it into memory.
/// </summary>
/// <remarks>
/// The game's <c>gdFsInit</c> reads only the first sector and assumes the entries are packed
/// back to back on 2048-byte boundaries from the first entry's offset, so <see cref="AfsWriter"/>
/// always writes them that way.
/// </remarks>
public sealed class AfsArchive : IDisposable
{
    public const int SectorSize = 2048;
    private static ReadOnlySpan<byte> Magic => "AFS\0"u8;

    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly AfsEntry[] _entries;

    public IReadOnlyList<AfsEntry> Entries => _entries;
    public int Count => _entries.Length;
    public long Length => _stream.Length;

    /// <summary>Offset of the first entry; everything before it is the header.</summary>
    public long DataStart => _entries.Length == 0 ? SectorSize : _entries[0].Offset;

    private AfsArchive(Stream stream, bool ownsStream)
    {
        _stream = stream;
        _ownsStream = ownsStream;

        Span<byte> head = stackalloc byte[8];
        stream.Position = 0;
        stream.ReadExactly(head);
        if (!head[..4].SequenceEqual(Magic))
            throw new InvalidDataException("Not an AFS archive.");

        var count = BinaryPrimitives.ReadInt32LittleEndian(head[4..]);
        if (count < 0 || count > 65535)
            throw new InvalidDataException($"Unexpected AFS entry count {count}.");

        var table = new byte[count * 8];
        stream.ReadExactly(table);
        _entries = new AfsEntry[count];
        for (var i = 0; i < count; i++)
        {
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(i * 8));
            var size = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(i * 8 + 4));
            if (offset + (long)size > stream.Length)
                throw new InvalidDataException($"AFS entry {i} runs past the end of the archive.");
            _entries[i] = new AfsEntry(i, offset, (int)size);
        }
    }

    public static AfsArchive Open(string path) =>
        new(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16), ownsStream: true);

    public static AfsArchive FromStream(Stream stream) => new(stream, ownsStream: false);

    public byte[] ReadEntry(int index)
    {
        var entry = _entries[index];
        var data = new byte[entry.Size];
        lock (_stream)
        {
            _stream.Position = entry.Offset;
            _stream.ReadExactly(data);
        }
        return data;
    }

    /// <summary>Reads the header (everything before the first entry).</summary>
    public byte[] ReadHeader()
    {
        var data = new byte[DataStart];
        lock (_stream)
        {
            _stream.Position = 0;
            _stream.ReadExactly(data);
        }
        return data;
    }

    internal void CopyEntryTo(int index, Stream destination)
    {
        var entry = _entries[index];
        var buffer = new byte[1 << 16];
        lock (_stream)
        {
            _stream.Position = entry.Offset;
            var remaining = entry.Size;
            while (remaining > 0)
            {
                var n = _stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                if (n <= 0)
                    throw new EndOfStreamException();
                destination.Write(buffer, 0, n);
                remaining -= n;
            }
        }
    }

    public void Dispose()
    {
        if (_ownsStream)
            _stream.Dispose();
    }
}

public readonly record struct AfsEntry(int Index, long Offset, int Size);

/// <summary>
/// Writes an AFS archive: the same entries in the same order, packed back to back on
/// 2048-byte boundaries, with some entries replaced.
/// </summary>
public static class AfsWriter
{
    public static void Write(AfsArchive source, IReadOnlyDictionary<int, byte[]> replacements, Stream output)
    {
        var count = source.Count;
        foreach (var index in replacements.Keys)
        {
            if (index < 0 || index >= count)
                throw new ArgumentOutOfRangeException(nameof(replacements), $"No AFS entry {index}.");
        }

        var dataStart = source.DataStart;
        var tableEnd = 8 + count * 8;
        if (dataStart < tableEnd || dataStart % AfsArchive.SectorSize != 0)
            throw new InvalidDataException("Unexpected AFS header layout.");

        // Lay out the entries.
        var offsets = new long[count];
        var sizes = new int[count];
        var position = dataStart;
        for (var i = 0; i < count; i++)
        {
            sizes[i] = replacements.TryGetValue(i, out var data) ? data.Length : source.Entries[i].Size;
            offsets[i] = position;
            position = Align(position + sizes[i]);
            if (position > uint.MaxValue)
                throw new InvalidDataException("AFS archive would exceed 4 GB.");
        }

        // Header: keep the original bytes (magic, count, anything after the table), new table.
        var header = source.ReadHeader();
        for (var i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8 + i * 8), (uint)offsets[i]);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8 + i * 8 + 4), (uint)sizes[i]);
        }
        output.Write(header);

        var padding = new byte[AfsArchive.SectorSize];
        for (var i = 0; i < count; i++)
        {
            if (replacements.TryGetValue(i, out var data))
                output.Write(data);
            else
                source.CopyEntryTo(i, output);

            var pad = (int)(Align(sizes[i]) - sizes[i]);
            output.Write(padding, 0, pad);
        }
    }

    public static void Write(AfsArchive source, IReadOnlyDictionary<int, byte[]> replacements, string path)
    {
        var temp = path + ".tmp";
        using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
        {
            Write(source, replacements, fs);
        }
        File.Move(temp, path, overwrite: true);
    }

    private static long Align(long value) =>
        (value + AfsArchive.SectorSize - 1) / AfsArchive.SectorSize * AfsArchive.SectorSize;
}
