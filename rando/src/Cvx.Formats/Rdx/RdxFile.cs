// Ported from BioRand's biohazard-utils (RdtCv.cs, RdtCv.Builder.cs), MIT,
// Copyright (c) 2022-2026 Ted John.

using System.Runtime.InteropServices;

namespace Cvx.Formats.Rdx;

/// <summary>
/// An expanded Code: Veronica room file (<c>RM_xxxx.RDX</c> after <see cref="Prs.Decompress"/>).
/// </summary>
/// <remarks>
/// Layout: <c>RDT_WORK</c> header at 0; at <c>hed00</c> (always 0x80) the <c>ROM_WORK</c> table
/// with 16 table offsets, 16 unused pointers and 14 counts (at 0x100), then the rest of
/// <c>ROM_WORK</c> (fog, ambient light...) up to 0x46C. The tables follow from 0x46C, then models
/// (64-aligned), motions (64-aligned), scripts (16-aligned) and textures (4-aligned).
///
/// Small changes that keep every table the same size (an item's type, a door's target, a
/// script byte) should use <see cref="WithPatch"/> so the rest of the file stays untouched.
/// <see cref="ToBuilder"/> rebuilds the whole file; it reproduces the original bytes when
/// nothing is changed.
/// </remarks>
public sealed class RdxFile
{
    public const int RomWorkOffset = 0x80;
    public const int CountsOffset = 0x100;
    public const int AfterCountsOffset = 0x180;
    public const int TablesStart = 0x46C;
    public const float MinimumVersion = 1.72f;

    // Table index in ROM_WORK -> chunk. Index 12 is evtp, which the game ignores (it uses
    // hed03 instead), so it is kept as a raw value.
    private static readonly RdxChunkKind[] TableKinds =
    [
        RdxChunkKind.Camera,    // cutp
        RdxChunkKind.Lighting,  // lgtp
        RdxChunkKind.Enemy,     // enep
        RdxChunkKind.Object,    // objp
        RdxChunkKind.Item,      // itmp
        RdxChunkKind.Effect,    // effp
        RdxChunkKind.Boundary,  // walp
        RdxChunkKind.Aot,       // etcp
        RdxChunkKind.Trigger,   // flrp
        RdxChunkKind.Player,    // posp
        RdxChunkKind.Event,     // rutp
        RdxChunkKind.Unknown1,  // ruttp
        RdxChunkKind.Unknown2,  // evtp (unused)
        RdxChunkKind.Action,    // evcp
        RdxChunkKind.Text,      // mesp
        RdxChunkKind.Sysmes,    // evlp
    ];

    private readonly byte[] _data;
    private readonly RdxChunks _chunks;

    public ReadOnlyMemory<byte> Data => _data;
    public int Length => _data.Length;

    public RdxFile(byte[] data)
    {
        _data = data;
        if (data.Length < TablesStart)
            throw new InvalidDataException("Room file is too small.");

        var header = Header;
        if (header.TableOffset != RomWorkOffset)
            throw new InvalidDataException($"Unexpected ROM_WORK offset 0x{header.TableOffset:X}.");

        _chunks = new RdxChunks(_data);
        var offsets = TableOffsets;
        for (var i = 0; i < TableKinds.Length; i++)
        {
            if (TableKinds[i] == RdxChunkKind.Unknown2 || offsets[i] == 0)
                continue;
            _chunks.Register(TableKinds[i], offsets[i]);
        }
        _chunks.Register(RdxChunkKind.Header, 0);
        _chunks.Register(RdxChunkKind.TableOffsets, header.TableOffset);
        _chunks.Register(RdxChunkKind.TableCounts, CountsOffset);
        _chunks.Register(RdxChunkKind.AfterCounts, AfterCountsOffset);
        _chunks.Register(RdxChunkKind.Model, header.ModelOffset);
        _chunks.Register(RdxChunkKind.Motion, header.MotionOffset);
        _chunks.Register(RdxChunkKind.Script, header.ScriptOffset);
        _chunks.Register(RdxChunkKind.Texture, header.TextureOffset);
    }

    public RdxHeader Header => MemoryMarshal.Read<RdxHeader>(_data);

    private ReadOnlySpan<int> TableOffsets => MemoryMarshal.Cast<byte, int>(_data.AsSpan(RomWorkOffset, 16 * 4));
    private ReadOnlySpan<int> Counts => MemoryMarshal.Cast<byte, int>(_data.AsSpan(CountsOffset, 14 * 4));

    public ReadOnlyMemory<byte> AfterCounts => _chunks.Memory(RdxChunkKind.AfterCounts);
    public RdxCameraList Cameras => new(Counts[0], _chunks.Memory(RdxChunkKind.Camera));
    public ReadOnlyMemory<byte> Lighting => _chunks.Memory(RdxChunkKind.Lighting);
    public int LightingCount => Counts[1];
    public ReadOnlySpan<RdxEnemy> Enemies => Table<RdxEnemy>(RdxChunkKind.Enemy, 2);
    public ReadOnlySpan<RdxObject> Objects => Table<RdxObject>(RdxChunkKind.Object, 3);
    public ReadOnlySpan<RdxItem> Items => Table<RdxItem>(RdxChunkKind.Item, 4);
    public ReadOnlySpan<RdxEffect> Effects => Table<RdxEffect>(RdxChunkKind.Effect, 5);
    public ReadOnlySpan<RdxBoundary> Boundaries => Table<RdxBoundary>(RdxChunkKind.Boundary, 6);
    public ReadOnlySpan<RdxAot> Aots => Table<RdxAot>(RdxChunkKind.Aot, 7);
    public ReadOnlySpan<RdxTrigger> Triggers => Table<RdxTrigger>(RdxChunkKind.Trigger, 8);
    public ReadOnlySpan<RdxPlayer> Players => Table<RdxPlayer>(RdxChunkKind.Player, 9);
    public ReadOnlySpan<RdxEvent> Events => Table<RdxEvent>(RdxChunkKind.Event, 10);
    public ReadOnlyMemory<byte> Unknown1Data => _chunks.Memory(RdxChunkKind.Unknown1);
    public int Unknown1Count => Counts[11];
    public int Unknown2 => TableOffsets[12];
    public int Unknown2Count => Counts[12];
    public int ReactionCount => Counts[13];
    public ReadOnlySpan<RdxReaction> Reactions => Table<RdxReaction>(RdxChunkKind.Action, -1);
    public RdxMessageList Text => new(_chunks.Memory(RdxChunkKind.Text));
    public ReadOnlyMemory<byte> Sysmes => _chunks.Memory(RdxChunkKind.Sysmes);
    public RdxPageList Models => new(Header.ModelOffset, _chunks.Memory(RdxChunkKind.Model));
    public RdxPageList Motions => new(Header.MotionOffset, _chunks.Memory(RdxChunkKind.Motion));
    public RdxScript Script => new(_chunks.Memory(RdxChunkKind.Script));
    public RdxTextureList Textures => new(Header.TextureOffset, _chunks.Memory(RdxChunkKind.Texture));

    /// <summary>File offset of the table that <paramref name="kind"/> lives in, or -1.</summary>
    public int TableOffsetOf(RdxTable kind) => kind switch
    {
        RdxTable.Enemies => TableOffsets[2],
        RdxTable.Objects => TableOffsets[3],
        RdxTable.Items => TableOffsets[4],
        RdxTable.Aots => TableOffsets[7],
        RdxTable.Players => TableOffsets[9],
        _ => -1,
    } is var o && o != 0 ? o : -1;

    /// <summary>File offset of the room scripts (<c>hed03</c>).</summary>
    public int ScriptOffset => Header.ScriptOffset;

    /// <summary>Returns a copy with <paramref name="bytes"/> written at <paramref name="offset"/>.</summary>
    public RdxFile WithPatch(int offset, ReadOnlySpan<byte> bytes)
    {
        if (offset < 0 || offset + bytes.Length > _data.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        var copy = (byte[])_data.Clone();
        bytes.CopyTo(copy.AsSpan(offset));
        return new RdxFile(copy);
    }

    /// <summary>Returns a copy with one record of a table replaced.</summary>
    public RdxFile WithRecord<T>(RdxTable table, int index, T value) where T : unmanaged
    {
        var offset = TableOffsetOf(table);
        if (offset < 0)
            throw new InvalidOperationException($"{table} table is empty.");
        var size = Marshal.SizeOf<T>();
        return WithPatch(offset + index * size, MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in value)));
    }

    private ReadOnlySpan<T> Table<T>(RdxChunkKind kind, int countIndex) where T : unmanaged
    {
        var memory = _chunks.Memory(kind);
        var size = Marshal.SizeOf<T>();
        var count = countIndex >= 0 ? Counts[countIndex] : memory.Length / size;
        count = Math.Min(count, memory.Length / size);
        return MemoryMarshal.Cast<byte, T>(memory.Span[..(count * size)]);
    }

    public Builder ToBuilder() => new()
    {
        Header = Header,
        AfterCounts = AfterCounts.ToArray(),
        Cameras = Cameras,
        Lighting = Lighting.ToArray(),
        LightingCount = LightingCount,
        Enemies = [.. Enemies],
        Objects = [.. Objects],
        Items = [.. Items],
        Effects = [.. Effects],
        Boundaries = [.. Boundaries],
        Aots = [.. Aots],
        Triggers = [.. Triggers],
        Players = [.. Players],
        Events = [.. Events],
        Unknown1Data = Unknown1Data.ToArray(),
        Unknown1Count = Unknown1Count,
        Unknown2 = Unknown2,
        Unknown2Count = Unknown2Count,
        ReactionCount = ReactionCount,
        Reactions = [.. Reactions],
        Text = Text,
        Sysmes = Sysmes.ToArray(),
        Models = Models,
        Motions = Motions,
        Script = Script,
        Textures = Textures,
    };

    public sealed class Builder
    {
        public RdxHeader Header { get; set; }
        public byte[] AfterCounts { get; set; } = [];
        public RdxCameraList Cameras { get; set; }
        public byte[] Lighting { get; set; } = [];
        public int LightingCount { get; set; }
        public List<RdxEnemy> Enemies { get; set; } = [];
        public List<RdxObject> Objects { get; set; } = [];
        public List<RdxItem> Items { get; set; } = [];
        public List<RdxEffect> Effects { get; set; } = [];
        public List<RdxBoundary> Boundaries { get; set; } = [];
        public List<RdxAot> Aots { get; set; } = [];
        public List<RdxTrigger> Triggers { get; set; } = [];
        public List<RdxPlayer> Players { get; set; } = [];
        public List<RdxEvent> Events { get; set; } = [];
        public byte[] Unknown1Data { get; set; } = [];
        public int Unknown1Count { get; set; }
        public int Unknown2 { get; set; }
        public int Unknown2Count { get; set; }
        public int ReactionCount { get; set; }
        public List<RdxReaction> Reactions { get; set; } = [];
        public RdxMessageList Text { get; set; }
        public byte[] Sysmes { get; set; } = [];
        public RdxPageList Models { get; set; }
        public RdxPageList Motions { get; set; }
        public RdxScript Script { get; set; }
        public RdxTextureList Textures { get; set; }

        public RdxFile ToFile()
        {
            var header = Header;
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.WriteStruct(header);

            ms.Position = CountsOffset;
            bw.Write(Cameras.Count);
            bw.Write(LightingCount);
            bw.Write(Enemies.Count);
            bw.Write(Objects.Count);
            bw.Write(Items.Count);
            bw.Write(Effects.Count);
            bw.Write(Boundaries.Count);
            bw.Write(Aots.Count);
            bw.Write(Triggers.Count);
            bw.Write(Players.Count);
            bw.Write(Events.Count);
            bw.Write(Unknown1Count);
            bw.Write(Unknown2Count);
            bw.Write(ReactionCount);

            ms.Position = AfterCountsOffset;
            bw.Write(AfterCounts);
            bw.Flush();
            if (ms.Position > TablesStart)
                throw new InvalidDataException("ROM_WORK data runs into the tables.");
            ms.Position = TablesStart;

            var tableOffsets = new int[16];
            void Table(int index, int length, Action write)
            {
                if (length == 0)
                    return;
                bw.Flush();
                tableOffsets[index] = (int)ms.Position;
                write();
            }

            Table(0, Cameras.Data.Length, () => bw.Write(Cameras.Data.Span));
            Table(1, Lighting.Length, () => bw.Write(Lighting));
            Table(2, Enemies.Count, () => bw.WriteStructs(Enemies));
            Table(3, Objects.Count, () => bw.WriteStructs(Objects));
            Table(4, Items.Count, () => bw.WriteStructs(Items));
            Table(5, Effects.Count, () => bw.WriteStructs(Effects));
            Table(6, Boundaries.Count, () => bw.WriteStructs(Boundaries));
            Table(7, Aots.Count, () => bw.WriteStructs(Aots));
            Table(8, Triggers.Count, () => bw.WriteStructs(Triggers));
            Table(9, Players.Count, () => bw.WriteStructs(Players));
            Table(10, Events.Count, () => bw.WriteStructs(Events));
            Table(11, Unknown1Data.Length, () => bw.Write(Unknown1Data));
            tableOffsets[12] = Unknown2;
            Table(13, Reactions.Count, () => bw.WriteStructs(Reactions));
            Table(14, Text.Data.Length, () => bw.Write(Text.Data.Span));
            Table(15, Sysmes.Length, () => bw.Write(Sysmes));

            bw.Align(64);
            if (Models.Data.Length != 0)
            {
                header.ModelOffset = (int)ms.Position;
                bw.Write(Models.WithNewBaseOffset(header.ModelOffset).Data.Span);
            }

            bw.Align(64);
            if (Motions.Data.Length != 0)
            {
                header.MotionOffset = (int)ms.Position;
                bw.Write(Motions.WithNewBaseOffset(header.MotionOffset).Data.Span);
            }

            bw.Align(16);
            if (Script.Data.Length != 0)
            {
                header.ScriptOffset = (int)ms.Position;
                bw.Write(Script.Data.Span);
            }

            bw.Align(4);
            if (Textures.Data.Length != 0)
            {
                header.TextureOffset = (int)ms.Position;
                bw.Write(Textures.WithNewBaseOffset(header.TextureOffset).Data.Span);
            }

            bw.Flush();
            ms.Position = RomWorkOffset;
            header.TableOffset = RomWorkOffset;
            foreach (var offset in tableOffsets)
                bw.Write(offset);

            ms.Position = 0;
            bw.WriteStruct(header);
            bw.Flush();

            Header = header;
            return new RdxFile(ms.ToArray());
        }
    }
}

public enum RdxTable
{
    Enemies,
    Objects,
    Items,
    Aots,
    Players,
}
