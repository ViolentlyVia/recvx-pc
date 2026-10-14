using System.Buffers.Binary;

namespace Cvx.Formats.Rdx;

/// <summary>One enemy of a room with the data the game loads for it.</summary>
/// <param name="Model">Its model page (<c>bhSetEneMdl</c>); empty when the entry has no model (flg bit 0 clear).</param>
/// <param name="Motion">Its motion block (<c>bhSetEneMtn</c>); empty when it shares the motions of an earlier enemy of its type.</param>
/// <param name="Textures">Indexes of the texture entries this enemy brings (the first enemy of a type and model version).</param>
public sealed record RdxEnemyAssets(int Index, RdxEnemy Entry, ReadOnlyMemory<byte> Model, ReadOnlyMemory<byte> Motion, IReadOnlyList<int> Textures)
{
    public int Type => Entry.Type;
    public int ModelVersion => (Entry.Variant >> 8) & 0xFF;
    public bool HasModel => (Entry.Header & 1) != 0;
}

/// <summary>
/// How a room's enemies map to its model, motion and texture lists, following the game's
/// loader (room.c: <c>bhSetRDT</c>, <c>bhSetEneMdl</c>, <c>bhFinishRoom</c>):
/// <list type="bullet">
/// <item>models (<c>hed01</c>): the room, one page per enemy with flg bit 0, one per object from
///   the fifth, one per item;</item>
/// <item>motions (<c>hed02</c>): one entry per enemy work (0 = use the motions an earlier enemy
///   of the same type loaded), then the room's, then two more;</item>
/// <item>textures (<c>hed04</c>): the room's, then one per textured model chunk the first time a
///   (type, model version, chunk) is seen, then objects, items and effects.</item>
/// </list>
/// Model pages are relocated in place when loaded, so every enemy needs its own copy.
/// </summary>
public sealed class RdxEnemyLayout
{
    private const uint MaskMagic = 0x4B53414D;   // "MASK"
    private const uint SkinMagic = 0x4E494B53;   // "SKIN"

    public required IReadOnlyList<RdxEnemyAssets> Enemies { get; init; }
    /// <summary>Model pages: index 0 is the room's; enemies follow.</summary>
    public required ReadOnlyMemory<byte>[] ModelPages { get; init; }
    public required ReadOnlyMemory<byte>[] MotionEntries { get; init; }
    public required ReadOnlyMemory<byte>[] TextureGroups { get; init; }
    public required int EnemyModelPages { get; init; }
    /// <summary>Texture entries before the enemies' (the room's, 0 or 1).</summary>
    public required int RoomTextures { get; init; }
    public required int EnemyTextures { get; init; }
    /// <summary>Texture entries the loader would read for objects, items and effects.</summary>
    public required int OtherTextures { get; init; }
    /// <summary>Where the items' texture entries start, and how many the loader reads for them.</summary>
    public required int ItemTextureStart { get; init; }
    public required int ItemTextures { get; init; }
    /// <summary>The model page of each item, and whether it has textures.</summary>
    public required IReadOnlyList<(int Page, bool Textured)> ItemModels { get; init; }

    /// <summary>Whether the counts add up to the room's lists (the layout is understood).</summary>
    public bool IsConsistent =>
        TextureGroups.Length == RoomTextures + EnemyTextures + OtherTextures &&
        (Enemies.Count(e => e.HasModel) == 0 || MotionEntries.Length >= Enemies.Count(e => e.HasModel) + 3);

    public static RdxEnemyLayout Read(RdxFile file)
    {
        var models = file.Models.Pages;
        var motions = file.Motions.Pages;
        var textures = file.Textures.Groups;
        var enemies = file.Enemies.ToArray();

        var roomTextures = models.Length != 0 && ModelHasTextures(models[0].Span, 0) ? 1 : 0;
        var textureIndex = roomTextures;
        var seen = new HashSet<int>();
        var result = new List<RdxEnemyAssets>();
        var page = 1;
        var work = 0;
        for (var i = 0; i < enemies.Length; i++)
        {
            var e = enemies[i];
            if ((e.Header & 1) == 0)
            {
                result.Add(new RdxEnemyAssets(i, e, default, default, []));
                continue;
            }
            var model = page < models.Length ? models[page] : default;
            page++;
            var motion = work < motions.Length ? motions[work] : default;
            work++;

            var mine = new List<int>();
            var chunk = -1;
            foreach (var textured in ModelChunks(model.Span))
            {
                if (!textured)
                    continue;
                chunk++;
                if (seen.Add(TextureKey(e.Type, (e.Variant >> 8) & 0xFF, chunk)))
                    mine.Add(textureIndex++);
            }
            result.Add(new RdxEnemyAssets(i, e, model, motion, mine));
        }
        var enemyPages = page - 1;
        var enemyTextures = textureIndex - roomTextures;

        // Objects (from the fifth), items and effects, for the consistency check.
        var other = 0;
        var objects = file.Objects;
        var objectIds = new HashSet<int>();
        for (var i = 4; i < objects.Length; i++, page++)
        {
            if (page < models.Length && ObjectHasTextures(models[page].Span) && objectIds.Add(objects[i].Flags1 & 0xFFFF))
                other++;
        }
        var itemStart = roomTextures + enemyTextures + other;
        var items = file.Items;
        var itemIds = new HashSet<int>();
        var itemModels = new List<(int, bool)>();
        for (var i = 0; i < items.Length; i++, page++)
        {
            var textured = page < models.Length && ModelHasTextures(models[page].Span, 0);
            itemModels.Add((page, textured));
            if (textured && itemIds.Add(items[i].ItemId))
                other++;
        }
        var itemTextures = roomTextures + enemyTextures + other - itemStart;
        var effectIds = new HashSet<int>();
        foreach (var effect in file.Effects)
        {
            var id = effect.Flags1 & 0xFFFF;
            if (((id >= 30 && id < 100) || id >= 400) && effectIds.Add(id))
                other++;
        }

        return new RdxEnemyLayout
        {
            Enemies = result,
            ModelPages = models,
            MotionEntries = motions,
            TextureGroups = textures,
            EnemyModelPages = enemyPages,
            RoomTextures = roomTextures,
            EnemyTextures = enemyTextures,
            OtherTextures = other,
            ItemTextureStart = itemStart,
            ItemTextures = itemTextures,
            ItemModels = itemModels,
        };
    }

    /// <summary>
    /// Where the game remembers an enemy texture: <c>sys->et_lp[type][model version][chunk]</c>,
    /// declared [100][16][16] and indexed without bounds, so model versions above 15 share
    /// slots with the next types. Textures are loaded once per slot.
    /// </summary>
    public static int TextureKey(int type, int modelVersion, int chunk) => (type * 16 + modelVersion) * 16 + chunk;

    /// <summary>The model chunks of an enemy page, as <c>bhSetEneMdl</c> walks it: whether each has textures.</summary>
    public static IEnumerable<bool> ModelChunks(ReadOnlySpan<byte> page)
    {
        var result = new List<bool>();
        var p = 0;
        while (p + 4 <= page.Length)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(page[p..]);
            if (size == 0xFFFFFFFF)
                break;
            p += 4;
            if (size == 0)
                continue;
            var magic = p + 4 <= page.Length ? BinaryPrimitives.ReadUInt32LittleEndian(page[p..]) : 0;
            if (magic != MaskMagic && magic != SkinMagic)
                result.Add(ModelHasTextures(page, p));
            p += (int)size;
        }
        return result;
    }

    private static bool ObjectHasTextures(ReadOnlySpan<byte> page)
    {
        if (page.Length < 16)
            return false;
        if (page[0] == 'M' && page[1] == 'D' && page[2] == 'L')
            return ModelHasTextures(page, 0);
        var size = BinaryPrimitives.ReadInt32LittleEndian(page);
        return ModelHasTextures(page, size + 8);
    }

    /// <summary>An MLB model (<c>bhMlbBinRealize</c>): its texture offset at +8 is -1 when it has none.</summary>
    private static bool ModelHasTextures(ReadOnlySpan<byte> data, int at) =>
        at >= 0 && at + 12 <= data.Length && BinaryPrimitives.ReadInt32LittleEndian(data[(at + 8)..]) != -1;
}
