// Ported from BioRand's biohazard-utils (RdtFileData.cs), MIT, Copyright (c) 2022-2026 Ted John.

namespace Cvx.Formats.Rdx;

internal enum RdxChunkKind
{
    Unknown,
    Header,
    TableOffsets,
    TableCounts,
    AfterCounts,
    Camera,
    Lighting,
    Enemy,
    Object,
    Item,
    Effect,
    Boundary,
    Aot,
    Trigger,
    Player,
    Event,
    Unknown1,
    Unknown2,
    Action,
    Text,
    Sysmes,
    Model,
    Motion,
    Script,
    Texture,
}

/// <summary>
/// A room file split into chunks by the offsets it contains. Lengths are not stored in the
/// file, so each chunk runs to the next known offset.
/// </summary>
internal sealed class RdxChunks
{
    private readonly List<Chunk> _chunks = [];
    private readonly ReadOnlyMemory<byte> _data;

    public RdxChunks(ReadOnlyMemory<byte> data) => _data = data;

    public IReadOnlyList<Chunk> All => _chunks;

    public void Register(RdxChunkKind kind, int offset)
    {
        if (offset < 0 || offset > _data.Length)
            throw new InvalidDataException($"{kind} offset 0x{offset:X} is outside the room file (0x{_data.Length:X}).");

        var position = FindInsertIndex(offset);
        if (position < _chunks.Count && _chunks[position].Offset == offset)
        {
            // Two chunks at the same offset: the earlier one becomes empty.
            var length = _chunks[position].Length;
            _chunks[position] = _chunks[position] with { Length = 0 };
            _chunks.Insert(position + 1, new Chunk(kind, offset, length));
        }
        else
        {
            var length = position == _chunks.Count
                ? _data.Length - offset
                : _chunks[position].Offset - offset;
            _chunks.Insert(position, new Chunk(kind, offset, length));
            if (position > 0)
            {
                var previous = _chunks[position - 1];
                _chunks[position - 1] = previous with { Length = offset - previous.Offset };
            }
        }
    }

    public Chunk? Find(RdxChunkKind kind)
    {
        var index = _chunks.FindIndex(x => x.Kind == kind);
        return index == -1 ? null : _chunks[index];
    }

    public ReadOnlyMemory<byte> Memory(RdxChunkKind kind) =>
        Find(kind) is { } chunk ? _data.Slice(chunk.Offset, chunk.Length) : ReadOnlyMemory<byte>.Empty;

    private int FindInsertIndex(int offset)
    {
        for (var i = 0; i < _chunks.Count; i++)
        {
            if (_chunks[i].Offset >= offset)
                return i;
        }
        return _chunks.Count;
    }

    public readonly record struct Chunk(RdxChunkKind Kind, int Offset, int Length)
    {
        public int End => Offset + Length;
    }
}
