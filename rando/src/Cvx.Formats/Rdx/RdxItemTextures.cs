namespace Cvx.Formats.Rdx;

/// <summary>
/// Keeps a room's item textures in step with its item ids. The loader (<c>bhSetRDT</c>) reads
/// one texture entry for the first pickup of each item id whose model has textures
/// (<c>sys->it_lp[id]</c>), so changing pickup ids can change how many entries it reads and
/// shift every texture after them. Each pickup keeps its own model, so it keeps the texture it
/// showed before.
/// </summary>
public static class RdxItemTextures
{
    /// <summary>
    /// <paramref name="patched"/> with its item texture entries rebuilt for its item ids, or
    /// <paramref name="patched"/> itself when the loader would read the same entries.
    /// </summary>
    /// <param name="originalIds">The room's item ids before they changed (the rest of the room is unchanged).</param>
    public static RdxFile Fix(RdxFile patched, IReadOnlyList<int> originalIds)
    {
        var before = RdxEnemyLayout.Read(patched);
        var newItems = patched.Items;
        if (newItems.Length != originalIds.Count || before.ItemModels.Count != originalIds.Count)
            throw new InvalidDataException("The item tables differ in size.");

        // The texture entry each pickup showed.
        var shown = new int[originalIds.Count];
        var byId = new Dictionary<int, int>();
        var next = before.ItemTextureStart;
        for (var i = 0; i < originalIds.Count; i++)
        {
            shown[i] = -1;
            if (!before.ItemModels[i].Textured)
                continue;
            if (!byId.TryGetValue(originalIds[i], out var entry))
                byId[originalIds[i]] = entry = next++;
            shown[i] = entry;
        }
        var oldCount = next - before.ItemTextureStart;

        // The entries the loader will read for the new ids.
        var wanted = new List<int>();
        var seen = new HashSet<int>();
        for (var i = 0; i < newItems.Length; i++)
        {
            if (before.ItemModels[i].Textured && seen.Add(newItems[i].ItemId))
                wanted.Add(shown[i]);
        }
        if (wanted.SequenceEqual(Enumerable.Range(before.ItemTextureStart, oldCount)))
            return patched;

        var groups = patched.Textures.Groups;
        var result = groups.Take(before.ItemTextureStart).ToList();
        result.AddRange(wanted.Select(i => groups[i]));
        result.AddRange(groups.Skip(before.ItemTextureStart + oldCount));

        var builder = patched.ToBuilder();
        builder.Textures = RdxTextureList.FromGroups(patched.Textures.BaseOffset, result, patched.Textures.HeaderSize);
        return builder.ToFile();
    }
}
