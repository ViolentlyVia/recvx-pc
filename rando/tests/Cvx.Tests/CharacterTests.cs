using System.IO.Compression;
using System.Numerics;
using Cvx.Formats;
using Cvx.Formats.Models;
using Cvx.Formats.Rdx;
using Cvx.Rando.Npcs;

namespace Cvx.Tests;

/// <summary>A test that needs BioRand's classic datapack; skipped when no "biorand" folder is found.</summary>
public sealed class BioRandFactAttribute : FactAttribute
{
    public BioRandFactAttribute()
    {
        if (BioRandData.ClassicPack == null)
            Skip = "BioRand's datapacks/classic.zip not found (a \"biorand\" folder next to or above the tests).";
        else if (TestData.Directory == null)
            Skip = "Game data not found (set CVX_DATA or set up ..\\recvx-pc\\build_pc\\data).";
    }
}

internal static class BioRandData
{
    public static string? ClassicPack { get; } = Find();

    private static string? Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "biorand", "datapacks", "classic.zip");
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    public static byte[] Read(string entry)
    {
        using var zip = ZipFile.OpenRead(ClassicPack!);
        using var stream = (zip.GetEntry(entry) ?? throw new FileNotFoundException(entry)).Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}

public class CharacterTests
{
    private const string Leon = "data/re2/pld0/leon.re2/PL00.PLD";

    [BioRandFact]
    public void ReadsAnRe2Character()
    {
        var pld = Pld.Read(BioRandData.Read(Leon));
        Assert.Equal(15, pld.Armature.Count);
        Assert.Equal(19, pld.Mesh.Parts.Count);
        Assert.Equal([1, 8, 9, 12], pld.Armature.Children[0]);
        // Y points down: the head is above the feet
        var rest = pld.Armature.RestPositions();
        Assert.True(rest[8].Y < rest[4].Y - 2000);
        Assert.Equal((512, 256, 8), (pld.Texture.Width, pld.Texture.Height, pld.Texture.BitsPerPixel));
        Assert.Equal(128, pld.Texture.PageWidth);
        Assert.All(pld.Mesh.Parts.Take(15).SelectMany(p => p.Polygons), p => Assert.InRange(p.Page, 0, 1));
    }

    [BioRandFact]
    public void LeonBecomesAnNpcModel()
    {
        using var rooms = RoomArchive.Open(TestData.Open());
        var room = RoomId.ParseName("RM_7220");
        var file = rooms.ReadRoom(room);
        var layout = RdxEnemyLayout.Read(file);
        var chris = layout.Enemies[1];
        Assert.Equal(92, chris.Type);
        var skeleton = ModelPage.Read(chris.Model.Span).Models.First();
        var template = TextureEntry.Read(layout.TextureGroups[chris.Textures[0]].Span).Pictures.First(p => p.Width == 256);

        var converted = CharacterConverter.Convert(Pld.Read(BioRandData.Read(Leon)), skeleton, template);
        var page = converted.Page.ToBytes();
        var model = Assert.Single(ModelPage.Read(page).Models);
        Assert.Equal(page, ModelPage.Read(page).ToBytes());

        // the skeleton is the target's; the 15 parts ride on their objects, nothing else has a model
        Assert.Equal(skeleton.Objects.Select(o => (o.Position, o.Child, o.Sibling)), model.Objects.Select(o => (o.Position, o.Child, o.Sibling)));
        var withModels = model.Objects.Select((o, i) => (o, i)).Where(x => x.o.Model != null).Select(x => x.i).ToList();
        Assert.Equal([1, 3, 5, 12, 13, 14, 16, 17, 18, 19, 20, 21, 23, 24, 25], withModels);
        Assert.All(withModels, i => Assert.Equal(0u, model.Objects[i].EvalFlags & NjObject.EvalHide));

        // he stands on the ground at about the target's height, facing -Z (his nose in front of his head)
        var bind = model.BindPositions();
        var points = withModels.SelectMany(i => model.Objects[i].Model!.Vertices![0].Ps2Vertices().Select(v => bind[i] + v.Position)).ToList();
        Assert.InRange(points.Min(p => p.Y), -0.5f, 0.5f);
        Assert.InRange(points.Max(p => p.Y), 16f, 20f);
        var head = model.Objects[5].Model!.Vertices![0].Ps2Vertices().Select(v => bind[5] + v.Position).ToList();
        Assert.True(head.Min(p => p.Z) < bind[5].Z - 0.5f);

        // textures: one per page and CLUT, with global indexes of their own
        var pictures = converted.Textures.Pictures.ToList();
        Assert.Equal(2, pictures.Count);
        Assert.Equal(model.Textures!.Count, pictures.Count);
        Assert.All(pictures, p => Assert.True(p.GlobalIndex >= CharacterConverter.FirstGlobalIndex));
        Assert.Equal(converted.Textures.ToBytes(), TextureEntry.Read(converted.Textures.ToBytes()).ToBytes());

        // the room takes it and still fits the game's room memory
        var donor = new RdxEnemyDonor(room, 1, chris.Type, chris.ModelVersion, chris.Entry.Effect, (byte)(chris.Entry.Index >> 8), page, chris.Motion, [converted.Textures.ToBytes()]);
        var result = RdxEnemyRewriter.Replace(file, new Dictionary<int, RdxEnemyDonor> { [1] = donor });
        var check = RdxEnemyLayout.Read(result);
        Assert.True(check.IsConsistent);
        // (the room's page list pads each page to 64 bytes)
        Assert.Equal(page, check.Enemies[1].Model.ToArray().Take(page.Length));
        Assert.True(Expand.FitsRoomPool(Prs.Compress(result.Data.Span), out _, out _));
    }

    [BioRandFact]
    public void ConvertedStripsFaceOutward()
    {
        // the game's winding (checked on its own models): a strip's even triangles (a, b, c) face
        // along cross(b - a, c - a), odd ones the other way, all flipped for a negative length
        using var rooms = RoomArchive.Open(TestData.Open());
        var layout = RdxEnemyLayout.Read(rooms.ReadRoom(RoomId.ParseName("RM_0030")));
        var steve = ModelPage.Read(layout.Enemies[2].Model.Span).Models.First();
        Assert.True(Outward(steve) > 0.99);

        var template = TextureEntry.Read(layout.TextureGroups[layout.Enemies[2].Textures[0]].Span).Pictures.First(p => p.Width == 256);
        var leon = CharacterConverter.Convert(Pld.Read(BioRandData.Read(Leon)), steve, template).Page.Models.First();
        Assert.True(Outward(leon) > 0.95);
    }

    /// <summary>Every texture of the game's rooms has a global index below the converted characters'.</summary>
    [GameDataFact]
    public void ConvertedTexturesHaveGlobalIndexesOfTheirOwn()
    {
        using var rooms = RoomArchive.Open(TestData.Open());
        var max = 0u;
        foreach (var id in RoomTable.All)
        {
            foreach (var group in rooms.ReadRoom(id).Textures.Groups)
            {
                TextureEntry entry;
                try
                {
                    entry = TextureEntry.Read(group.Span);
                }
                catch (InvalidDataException)
                {
                    continue;
                }
                foreach (var r in entry.Records.Where(r => r.Code == TextureRecord.Tim2Code && r.Data.Length >= 0x100))
                    max = Math.Max(max, new Tim2Picture(r.Data).GlobalIndex & 0x3FFFFFF);
            }
        }
        Assert.InRange(max, 1u, CharacterConverter.FirstGlobalIndex - 1);
    }

    private static double Outward(NjModel model)
    {
        int agree = 0, total = 0;
        foreach (var m in model.Objects.Select(o => o.Model).OfType<NjChunkModel>().Distinct())
        {
            var verts = new Dictionary<int, (Vector3 P, Vector3 N)>();
            foreach (var c in m.Vertices!.Where(c => c.IsPs2))
            {
                var n = c.IndexOffset;
                foreach (var v in c.Ps2Vertices())
                    verts[n++] = v;
            }
            foreach (var strip in m.Polygons!.Where(c => c.Type is >= 64 and <= 66).SelectMany(c => c.Strips()))
            {
                for (var i = 0; i + 2 < strip.Vertices.Count; i++)
                {
                    var (a, b, c) = (verts[strip.Vertices[i].Index], verts[strip.Vertices[i + 1].Index], verts[strip.Vertices[i + 2].Index]);
                    var face = Vector3.Cross(b.P - a.P, c.P - a.P);
                    if ((i % 2 == 1) != strip.Flipped)
                        face = -face;
                    if (Vector3.Dot(face, a.N + b.N + c.N) > 0)
                        agree++;
                    total++;
                }
            }
        }
        return (double)agree / total;
    }
}
