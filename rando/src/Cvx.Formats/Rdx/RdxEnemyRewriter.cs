namespace Cvx.Formats.Rdx;

/// <summary>
/// An enemy taken from another room: what its entry needs (type, model version, <c>type</c>
/// field, <c>prm1</c>) and the data the game loads for it.
/// </summary>
/// <param name="Motion">The motion block of its type (every enemy type needs one in a room).</param>
/// <param name="Textures">Its texture entries, one per textured model chunk, in chunk order.</param>
public sealed record RdxEnemyDonor(
    RoomId Room, int Index, int Type, int ModelVersion, short Effect, byte Prm1,
    ReadOnlyMemory<byte> Model, ReadOnlyMemory<byte> Motion, IReadOnlyList<ReadOnlyMemory<byte>> Textures)
{
    /// <summary>
    /// Takes enemy <paramref name="index"/> of a room as a donor, or null when that room does not
    /// hold everything it needs (its textures came with an earlier enemy, or no motions).
    /// </summary>
    public static RdxEnemyDonor? From(RoomId room, RdxFile file, int index, short? effect = null)
    {
        var layout = RdxEnemyLayout.Read(file);
        if (index >= layout.Enemies.Count)
            return null;
        var e = layout.Enemies[index];
        var textured = RdxEnemyLayout.ModelChunks(e.Model.Span).Count(t => t);
        if (!e.HasModel || e.Textures.Count != textured)
            return null;
        var motion = layout.Enemies.Where(x => x.Type == e.Type && x.Motion.Length != 0).Select(x => x.Motion).FirstOrDefault();
        if (motion.Length == 0)
            return null;
        return new RdxEnemyDonor(room, index, e.Type, e.ModelVersion, effect ?? e.Entry.Effect, (byte)(e.Entry.Index >> 8),
            e.Model, motion, e.Textures.Select(t => layout.TextureGroups[t]).ToArray());
    }
}

/// <summary>
/// One enemy of a rebuilt enemy list: one of the room's (<see cref="From"/>, its index), with a
/// donor's look when <see cref="Donor"/> is set, or a new enemy made from <see cref="Donor"/>.
/// </summary>
/// <param name="Position">A new place (a new enemy needs one).</param>
/// <param name="Direction">Its new facing (<c>ay</c>, 0x10000 a full turn).</param>
public sealed record RdxEnemySlot(int? From, RdxEnemyDonor? Donor = null, RdxVectorF? Position = null, int? Direction = null);

/// <summary>
/// Replaces enemies of a room with donors from other rooms, and moves, drops or adds enemies.
/// The model pages, the motion block of each type and the texture entries are rebuilt the way
/// the game's loader reads them (see <see cref="RdxEnemyLayout"/>); objects, items and effects
/// keep their data. Scripts are not touched: an enemy's index in the list is its work number
/// (<c>wrk_no</c>), which the scripts name it by, so a caller that drops or adds enemies fixes
/// those itself.
/// </summary>
public static class RdxEnemyRewriter
{
    public sealed class ConflictException(string message) : Exception(message);

    /// <summary>Gives enemies another look; their places, floors, work numbers and indexes stay.</summary>
    /// <exception cref="ConflictException">Two different enemies would share a texture slot.</exception>
    public static RdxFile Replace(RdxFile room, IReadOnlyDictionary<int, RdxEnemyDonor> replacements) =>
        Rebuild(room, room.Enemies.ToArray().Select((_, i) => new RdxEnemySlot(i, replacements.GetValueOrDefault(i))).ToList());

    /// <summary>
    /// Rebuilds the enemy list as <paramref name="slots"/>: the room's enemies named there in
    /// their order (those left out are dropped), with new enemies anywhere. Each enemy's work
    /// number becomes its new index.
    /// </summary>
    /// <exception cref="ConflictException">Two different enemies would share a texture slot.</exception>
    public static RdxFile Rebuild(RdxFile room, IReadOnlyList<RdxEnemySlot> slots)
    {
        var layout = RdxEnemyLayout.Read(room);
        if (!layout.IsConsistent || layout.Enemies.Any(e => !e.HasModel))
            throw new InvalidDataException($"The room's enemy data is not understood (textures {layout.RoomTextures}+{layout.EnemyTextures}+{layout.OtherTextures}/{layout.TextureGroups.Length}, motions {layout.MotionEntries.Length}).");
        var old = layout.Enemies;
        if (slots.Any(s => s.From == null && (s.Donor == null || s.Position == null)))
            throw new ArgumentException("A new enemy needs a donor and a position.", nameof(slots));
        var kept = slots.Where(s => s.From != null).Select(s => s.From!.Value).ToList();
        if (kept.Distinct().Count() != kept.Count || kept.Any(i => i < 0 || i >= old.Count) || !kept.SequenceEqual(kept.Order()))
            throw new ArgumentException("The room's enemies must keep their order, once each.", nameof(slots));
        var floors = Floors(room);

        // Entries.
        var entries = new List<RdxEnemy>();
        foreach (var s in slots)
        {
            var e = s.From is int from ? old[from].Entry : new RdxEnemy { Header = 1 };
            if (s.Donor is { } d)
            {
                e.Type = (short)d.Type;
                e.Variant = (short)((e.Variant & 0xFF) | (d.ModelVersion << 8));
                e.Effect = d.Effect;
                e.Index = (short)((e.Index & 0xFF) | (d.Prm1 << 8));
            }
            if (s.Position is { } p)
            {
                e.Position = p;
                e.Variant = (short)((e.Variant & ~0xFF) | (FloorOf(floors, p.Y) & 0xFF));
            }
            if (s.Direction is int ay)
                e.Rotation = new RdxVector32(0, ay, 0);
            e.Index = (short)((e.Index & ~0xFF) | entries.Count);
            entries.Add(e);
        }

        // Model pages: index 0 is the room's, then one per enemy, then objects and items.
        var pages = new List<ReadOnlyMemory<byte>> { layout.ModelPages[0] };
        pages.AddRange(slots.Select(s => s.Donor?.Model ?? old[s.From!.Value].Model));
        pages.AddRange(layout.ModelPages.Skip(1 + layout.EnemyModelPages));

        // Motions: one entry per enemy, then the room's (three when the room had no enemies).
        // The first enemy of each type carries the type's block; types the rebuild does not
        // touch keep their entries as they were.
        var motions = slots.Select(s => s.From is int from ? old[from].Motion : default).ToArray();
        var touched = slots.Where(s => s.Donor != null)
            .SelectMany(s => s.From is int from ? [old[from].Type, s.Donor!.Type] : new[] { s.Donor!.Type })
            .Concat(old.Where(e => !kept.Contains(e.Index)).Select(e => e.Type))
            .ToHashSet();
        for (var i = 0; i < entries.Count; i++)
        {
            if (touched.Contains(entries[i].Type) || (slots[i].From is int from && touched.Contains(old[from].Type)))
                motions[i] = default;
        }
        foreach (var type in touched)
        {
            var block = old.Where(e => e.Type == type && e.Motion.Length != 0).Select(e => e.Motion).FirstOrDefault();
            if (block.Length == 0)
                block = slots.Where(s => s.Donor?.Type == type).Select(s => s.Donor!.Motion).FirstOrDefault();
            var first = entries.FindIndex(e => e.Type == type);
            if (first >= 0)
                motions[first] = block;
        }
        var roomMotions = layout.MotionEntries.Skip(old.Count).ToList();
        if (old.Count == 0 && roomMotions.Count == 0)
            roomMotions.AddRange(new ReadOnlyMemory<byte>[3]);

        // Textures: the room's, then each texture slot the first time an enemy uses it.
        var oldBySlot = new Dictionary<int, ReadOnlyMemory<byte>>();
        var next = layout.RoomTextures;
        var oldSeen = new HashSet<int>();
        foreach (var e in old)
        {
            var chunk = -1;
            foreach (var textured in RdxEnemyLayout.ModelChunks(e.Model.Span))
            {
                if (!textured)
                    continue;
                chunk++;
                var key = RdxEnemyLayout.TextureKey(e.Type, e.ModelVersion, chunk);
                if (oldSeen.Add(key))
                    oldBySlot[key] = layout.TextureGroups[next++];
            }
        }
        var groups = layout.TextureGroups.Take(layout.RoomTextures).ToList();
        var owner = new Dictionary<int, (int Type, int Version, int Chunk)>();
        for (var i = 0; i < entries.Count; i++)
        {
            var type = (int)entries[i].Type;
            var version = (entries[i].Variant >> 8) & 0xFF;
            var donor = slots[i].Donor;
            var chunk = -1;
            foreach (var textured in RdxEnemyLayout.ModelChunks(pages[1 + i].Span))
            {
                if (!textured)
                    continue;
                chunk++;
                var key = RdxEnemyLayout.TextureKey(type, version, chunk);
                if (owner.TryGetValue(key, out var who))
                {
                    if (who != (type, version, chunk))
                        throw new ConflictException($"enemy {i} (type {type} version {version}) shares a texture slot with type {who.Type} version {who.Version}");
                    continue;
                }
                owner[key] = (type, version, chunk);
                if (donor != null)
                    groups.Add(donor.Textures[chunk]);
                else if (oldBySlot.TryGetValue(key, out var g))
                    groups.Add(g);
                else
                    throw new ConflictException($"enemy {i}: no texture for slot {key}");
            }
        }
        groups.AddRange(layout.TextureGroups.Skip(layout.RoomTextures + layout.EnemyTextures));

        var builder = room.ToBuilder();
        builder.Enemies = entries;
        builder.Models = RdxPageList.FromPages(room.Models.BaseOffset, pages, 64);
        builder.Motions = RdxPageList.FromPages(room.Motions.BaseOffset, [.. motions, .. roomMotions], 4);
        builder.Textures = RdxTextureList.FromGroups(room.Textures.BaseOffset, groups, room.Textures.HeaderSize);
        return builder.ToFile();
    }

    /// <summary>The room's floor heights (<c>ROM_WORK.grand</c>, 32 floats at 0x120).</summary>
    public static float[] Floors(RdxFile room)
    {
        var after = room.AfterCounts.Span;
        return after.Length < 0x20 + 32 * 4 ? new float[32]
            : System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(after.Slice(0x20, 32 * 4)).ToArray();
    }

    /// <summary>The floor a height is on, as the game works it out (pwksub.c <c>bhSetFloorNum</c>).</summary>
    public static int FloorOf(float[] floors, float y)
    {
        var fno = 2;
        for (var i = 0; i < 31; i++)
        {
            if ((floors[i] != 0 && y >= floors[i]) || (i == 2 && y >= floors[i]))
                fno = i;
        }
        return fno - 2;
    }
}
