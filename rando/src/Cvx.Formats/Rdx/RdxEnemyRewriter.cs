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
/// Replaces enemies of a room with donors from other rooms. Positions, floors, work numbers
/// and the scripts' enemy indexes stay; the model page, the motion block of each type and the
/// texture entries are rebuilt the way the game's loader reads them (see
/// <see cref="RdxEnemyLayout"/>). Objects, items and effects keep their data.
/// </summary>
public static class RdxEnemyRewriter
{
    public sealed class ConflictException(string message) : Exception(message);

    /// <exception cref="ConflictException">Two different enemies would share a texture slot.</exception>
    public static RdxFile Replace(RdxFile room, IReadOnlyDictionary<int, RdxEnemyDonor> replacements)
    {
        var layout = RdxEnemyLayout.Read(room);
        if (!layout.IsConsistent || layout.Enemies.Any(e => !e.HasModel))
            throw new InvalidDataException($"The room's enemy data is not understood (textures {layout.RoomTextures}+{layout.EnemyTextures}+{layout.OtherTextures}/{layout.TextureGroups.Length}, motions {layout.MotionEntries.Length}).");
        var old = layout.Enemies;

        // Entries.
        var entries = old.Select(e => e.Entry).ToList();
        foreach (var (i, d) in replacements)
        {
            var e = entries[i];
            e.Type = (short)d.Type;
            e.Variant = (short)((e.Variant & 0xFF) | (d.ModelVersion << 8));
            e.Effect = d.Effect;
            e.Index = (short)((e.Index & 0xFF) | (d.Prm1 << 8));
            entries[i] = e;
        }

        // Model pages: index 0 is the room's, then one per enemy.
        var pages = layout.ModelPages.ToArray();
        foreach (var (i, d) in replacements)
            pages[1 + i] = d.Model;

        // Motions: the first enemy of each type carries the type's block.
        var motions = layout.MotionEntries.ToArray();
        var touched = replacements.Keys.Select(i => (int)old[i].Type).Concat(replacements.Values.Select(d => d.Type)).ToHashSet();
        var blocks = touched.ToDictionary(t => t, t =>
        {
            var block = old.Where(e => e.Type == t && e.Motion.Length != 0).Select(e => e.Motion).FirstOrDefault();
            return block.Length != 0 ? block : replacements.Values.Where(d => d.Type == t).Select(d => d.Motion).FirstOrDefault();
        });
        for (var i = 0; i < entries.Count; i++)
        {
            if (touched.Contains(old[i].Type) || touched.Contains(entries[i].Type))
                motions[i] = default;
        }
        foreach (var type in touched)
        {
            var first = entries.FindIndex(e => e.Type == type);
            if (first >= 0)
                motions[first] = blocks[type];
        }

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
            replacements.TryGetValue(i, out var donor);
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
        builder.Motions = RdxPageList.FromPages(room.Motions.BaseOffset, motions, 4);
        builder.Textures = RdxTextureList.FromGroups(room.Textures.BaseOffset, groups, room.Textures.HeaderSize);
        return builder.ToFile();
    }
}
