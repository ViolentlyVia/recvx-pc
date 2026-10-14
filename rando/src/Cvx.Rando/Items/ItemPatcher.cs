using Cvx.Formats;
using Cvx.Formats.Rdx;
using Cvx.Formats.Script;
using Cvx.Rando.Data;

namespace Cvx.Rando.Items;

/// <summary>One change made to a room file, for the spoiler log and tests.</summary>
public sealed record RoomPatch(RoomId Room, int GlobalId, int From, int To, string Where);

/// <summary>
/// Writes item assignments into the room files. Every change keeps the file's layout (item ids
/// and script bytes are patched in place), so rooms never need rebuilding for items.
/// </summary>
public sealed class ItemPatcher
{
    private readonly RandoData _data;
    private readonly RoomArchive _archive;
    private readonly Dictionary<RoomId, byte[]> _rooms = [];

    public List<RoomPatch> Patches { get; } = [];

    public ItemPatcher(RandoData data, RoomArchive archive)
    {
        _data = data;
        _archive = archive;
    }

    /// <summary>The rooms that changed, ready for <see cref="RoomArchive.WriteModified"/>.</summary>
    public IReadOnlyDictionary<RoomId, RdxFile> ChangedRooms =>
        _rooms.ToDictionary(x => x.Key, x => new RdxFile(x.Value));

    private byte[] Room(RoomId id)
    {
        if (!_rooms.TryGetValue(id, out var data))
            _rooms[id] = data = _archive.ReadExpanded(id);
        return data;
    }

    public void Apply(ItemPlacementResult placement, RandoSettings settings)
    {
        foreach (var (key, room) in _data.Map.Rooms)
        {
            var id = RoomId.ParseBioRand(key);
            foreach (var item in room.Items ?? [])
            {
                if (item.DoorRando is bool dr && dr != settings.RandomDoors)
                    continue;
                if (item.GlobalId is not int g || item.Type is not int from)
                    continue;
                if (!placement.ByGlobalId.TryGetValue(g, out var assignment) || assignment.Type == from)
                    continue;
                PatchItem(id, item, from, assignment.Type);
            }
        }
        MirrorVariants();
    }

    private void PatchItem(RoomId id, MapRoomItem item, int from, int to)
    {
        var bytes = Room(id);
        var file = new RdxFile(bytes);
        var hasOffsets = item.Offsets is { Length: > 0 };

        // The room's item table, unless the item only exists in script offsets.
        if (item.ItemId is int slot || !hasOffsets)
        {
            slot = item.ItemId ?? ResolveSlot(file, item.Id, from) ?? ResolveSlot(file, item.Id, to)
                ?? throw new InvalidDataException($"{id.BioRandId} item {item.Id}: no item slot found");
            var offset = file.TableOffsetOf(RdxTable.Items) + slot * 36 + 4;   // ETTY_WORK.id
            var current = bytes[offset] | bytes[offset + 1] << 8;
            if (current != to)
            {
                if (current != from)
                    throw new InvalidDataException($"{id.BioRandId} item slot {slot} holds {current}, expected {from}");
                bytes[offset] = (byte)to;
                bytes[offset + 1] = (byte)(to >> 8);
                Patches.Add(new RoomPatch(id, item.GlobalId!.Value, from, to, $"item slot {slot}"));
            }
        }

        // Script instructions that give or check the item.
        if (hasOffsets)
        {
            var opcodes = CvScriptReader.ReadOpcodes(file).ToDictionary(o => o.Offset);
            foreach (var spec in item.Offsets!)
            {
                if (!opcodes.TryGetValue(spec.Start, out var op))
                    throw new InvalidDataException($"{id.BioRandId}: no opcode at {spec}");
                var patched = false;
                foreach (var p in CvOpcodes.ItemPositions(op))
                {
                    if (op.Bytes.Span[p] == from)
                    {
                        bytes[op.Offset + p] = (byte)to;
                        patched = true;
                    }
                }
                if (patched)
                    Patches.Add(new RoomPatch(id, item.GlobalId!.Value, from, to, $"{op.Name} at {spec}"));
                else if (!CvOpcodes.ItemPositions(op).Any(p => op.Bytes.Span[p] == to))
                    throw new InvalidDataException($"{id.BioRandId}: {op.Name} at {spec} does not hold item {from}");
            }
        }

        foreach (var spec in item.Nop ?? [])
            Nop(id, file, spec);
    }

    /// <summary>The item slot a trigger area picks up (from the room or its scripts), preferring one that holds <paramref name="type"/>.</summary>
    public static int? ResolveSlot(RdxFile file, int aotIndex, int type)
    {
        var states = AotStates.Read(file, CvScriptReader.ReadOpcodes(file)).Get(aotIndex)
            .Where(s => s.Type == RdxAot.KindItem).ToList();
        var items = file.Items;
        foreach (var s in states)
        {
            if (s.Prm0 < items.Length && items[s.Prm0].ItemId == type)
                return s.Prm0;
        }
        return null;
    }

    /// <summary>Blanks the opcodes in <paramref name="spec"/> with 0xF4 (a one-byte "nothing" opcode), as BioRand does.</summary>
    private void Nop(RoomId id, RdxFile file, OffsetSpec spec)
    {
        var bytes = Room(id);
        foreach (var op in CvScriptReader.ReadOpcodes(file).Where(o => spec.Contains(o.Offset)))
            Array.Fill(bytes, (byte)0xF4, op.Offset, op.Length);
    }

    /// <summary>
    /// Room variants missing from the room data (RM_3010 next to RM_3011, ...) hold the same
    /// pickups as a mapped variant: give them the same new items, matched by original item and
    /// position.
    /// </summary>
    private void MirrorVariants()
    {
        var mapped = _data.Map.Rooms.Keys.Select(RoomId.ParseBioRand).ToHashSet();
        var changes = Patches.Where(p => p.Where.StartsWith("item slot", StringComparison.Ordinal)).ToList();
        foreach (var variant in RoomTable.All.Where(r => !mapped.Contains(r)))
        {
            var siblings = changes.Where(p => p.Room.Stage == variant.Stage && p.Room.Room == variant.Room).ToList();
            if (siblings.Count == 0)
                continue;

            var bytes = Room(variant);
            var file = new RdxFile(bytes);
            var items = file.Items;
            var tableOffset = file.TableOffsetOf(RdxTable.Items);
            for (var i = 0; i < items.Length; i++)
            {
                foreach (var p in siblings)
                {
                    var source = new RdxFile(Room(p.Room)).Items;
                    var slot = int.Parse(p.Where["item slot ".Length..]);
                    var s = source[slot];
                    if (items[i].ItemId != p.From || Distance(items[i].Position, s.Position) > 2f)
                        continue;
                    var offset = tableOffset + i * 36 + 4;
                    bytes[offset] = (byte)p.To;
                    bytes[offset + 1] = (byte)(p.To >> 8);
                    Patches.Add(new RoomPatch(variant, p.GlobalId, p.From, p.To, $"item slot {i} (as {p.Room})"));
                    break;
                }
            }
        }
    }

    private static float Distance(RdxVectorF a, RdxVectorF b) =>
        MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));
}
