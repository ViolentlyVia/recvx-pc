using Cvx.Formats.Rdx;

namespace Cvx.Formats;

/// <summary>The rooms in RDX_LNK.AFS: PRS-compressed room files, one per <see cref="RoomTable"/> entry.</summary>
public sealed class RoomArchive : IDisposable
{
    private readonly AfsArchive _afs;

    public AfsArchive Afs => _afs;

    private RoomArchive(AfsArchive afs) => _afs = afs;

    public static RoomArchive Open(GameData data) => new(data.OpenRoomArchive());

    public byte[] ReadCompressed(RoomId id) => _afs.ReadEntry(IndexOf(id));

    public byte[] ReadExpanded(RoomId id) => Prs.Decompress(ReadCompressed(id));

    public RdxFile ReadRoom(RoomId id) => new(ReadExpanded(id));

    public int CompressedSize(RoomId id) => _afs.Entries[IndexOf(id)].Size;

    /// <summary>
    /// Writes a new RDX_LNK.AFS to <paramref name="path"/> with the given rooms compressed and
    /// replaced; every other room is copied unchanged.
    /// </summary>
    public void WriteModified(string path, IReadOnlyDictionary<RoomId, RdxFile> rooms)
    {
        var compressed = Compress(rooms.ToDictionary(x => x.Key, x => x.Value.Data));
        AfsWriter.Write(_afs, compressed.ToDictionary(x => IndexOf(x.Key), x => x.Value), path);
    }

    /// <summary>Compresses room files in parallel.</summary>
    public static Dictionary<RoomId, byte[]> Compress(IReadOnlyDictionary<RoomId, ReadOnlyMemory<byte>> rooms)
    {
        var keys = rooms.Keys.ToArray();
        var results = new byte[keys.Length][];
        Parallel.For(0, keys.Length, i => results[i] = Prs.Compress(rooms[keys[i]].Span));
        return keys.Select((k, i) => (k, results[i])).ToDictionary(x => x.k, x => x.Item2);
    }

    private static int IndexOf(RoomId id)
    {
        var index = RoomTable.IndexOf(id);
        if (index < 0)
            throw new ArgumentException($"{id} is not in {GameData.RoomArchiveName}.", nameof(id));
        return index;
    }

    public void Dispose() => _afs.Dispose();
}
