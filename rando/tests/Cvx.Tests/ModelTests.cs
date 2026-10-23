using System.Buffers.Binary;
using System.Numerics;
using Cvx.Formats;
using Cvx.Formats.Models;
using Cvx.Formats.Rdx;

namespace Cvx.Tests;

public class ModelTests
{
    private static NjModel TwoBoneModel()
    {
        var mesh = new NjChunkModel
        {
            Vertices = [NjVertexChunk.Ps2([(new Vector3(0, 0, 0), Vector3.UnitZ), (new Vector3(1, 0, 0), Vector3.UnitZ), (new Vector3(0, 1, 0), Vector3.UnitZ)])],
            Polygons =
            [
                NjPolyChunk.Material(0xFF7F7F7F, 0xFF7F7F7F),
                NjPolyChunk.Texture(1),
                NjStrip.Write(NjPolyChunk.StripUvhType, 0x02, [new NjStrip(false, [new(0, 0, 0), new(1, 1023, 0), new(2, 0, 1023)])]),
            ],
            Center = new Vector3(0.5f, 0.5f, 0),
            Radius = 1,
        };
        var model = new NjModel { Textures = ["BODY", "HEAD"] };
        model.Objects.Add(new NjObject { EvalFlags = 0xF, Child = 1 });
        model.Objects.Add(new NjObject { EvalFlags = 0x6, Model = mesh, Position = new Vector3(0, 10, 0) });
        return model;
    }

    [Fact]
    public void AModelWritesAndReadsBack()
    {
        var page = new ModelPage();
        page.Blocks.Add(ModelPageBlock.FromModel(TwoBoneModel()));
        var bytes = page.ToBytes();

        var read = ModelPage.Read(bytes);
        Assert.Equal(bytes, read.ToBytes());
        var model = Assert.Single(read.Models);
        Assert.Equal(["BODY", "HEAD"], model.Textures);
        Assert.Equal(2, model.Objects.Count);
        Assert.Equal(1, model.Objects[0].Child);
        Assert.Equal(new Vector3(0, 10, 0), model.BindPositions()[1]);
        var mesh = model.Objects[1].Model!;
        Assert.Equal(1, mesh.Radius);
        Assert.Equal(3, mesh.VertexCount);
        Assert.Equal(new Vector3(1, 0, 0), mesh.Vertices![0].Ps2Vertices().ElementAt(1).Position);
        var strip = Assert.Single(mesh.Polygons!.Single(c => c.Kind == NjPolyKind.Strip).Strips());
        Assert.Equal([0, 1, 2], strip.Vertices.Select(v => (int)v.Index));
        Assert.Equal(1023, strip.Vertices[1].U);
        Assert.Equal(1, mesh.Polygons!.Single(c => c.Type == NjPolyChunk.TextureType).TextureId);

        // the vertex list starts on a 64-byte boundary of the page; blocks are whole 64s
        var mlb = 4;
        var body = mlb + 16;
        var objects = body + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(mlb + 12));
        var modelAt = body + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(objects + 0x34 + 4));
        var vlist = body + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(modelAt));
        Assert.Equal(0, vlist % 64);
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(bytes) % 64);
        Assert.True(BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(modelAt + 20)) < 0);
    }

    [Fact]
    public void StripsWriteTheirUserData()
    {
        var strips = new[]
        {
            new NjStrip(true, [new(5, 1, 2), new(6, 3, 4), new(7, 5, 6, [9, 0]), new(8, 7, 8, [1, 0])]),
            new NjStrip(false, [new(1, 0, 0), new(2, 0, 0), new(3, 0, 0, [0, 0])]),
        };
        var chunk = NjStrip.Write(NjPolyChunk.StripUvnType, 0, strips);
        Assert.Equal(0, (4 + chunk.Body.Length) % 4);
        var read = chunk.Strips();
        Assert.True(read[0].Flipped);
        Assert.Equal([5, 6, 7, 8], read[0].Vertices.Select(v => (int)v.Index));
        Assert.Equal([9, 0], read[0].Vertices[2].User);
        Assert.Equal(3, read[1].Vertices.Count);
    }

    [Fact]
    public void PalettesUseTheGsOrder()
    {
        Assert.Equal(16, Tim2Picture.Csm1(8));
        Assert.Equal(8, Tim2Picture.Csm1(16));
        Assert.Equal(7, Tim2Picture.Csm1(7));
        Assert.Equal(255, Tim2Picture.Csm1(255));
        Assert.All(Enumerable.Range(0, 256), i => Assert.Equal(i, Tim2Picture.Csm1(Tim2Picture.Csm1(i))));
    }

    /// <summary>Every enemy model page (NPCs included) and texture entry of every room.</summary>
    [GameDataFact]
    public void TheGamesModelsWriteBackByteForByte()
    {
        using var rooms = RoomArchive.Open(TestData.Open());
        var pages = 0;
        var strips = 0;
        var textures = 0;
        var failures = new List<string>();
        foreach (var id in RoomTable.All)
        {
            var layout = RdxEnemyLayout.Read(rooms.ReadRoom(id));
            foreach (var e in layout.Enemies.Where(e => e.HasModel))
            {
                var where = $"{id.Name} enemy {e.Index} (type {e.Type})";
                try
                {
                    var page = ModelPage.Read(e.Model.Span);
                    var written = page.ToBytes();
                    if (!written.AsSpan().SequenceEqual(e.Model.Span))
                        failures.Add($"{where}: page differs at 0x{FirstDifference(written, e.Model.Span):X} ({written.Length} vs {e.Model.Length})");
                    pages++;
                    foreach (var chunk in page.Models.SelectMany(m => m.Objects).Select(o => o.Model).OfType<NjChunkModel>().Distinct()
                                 .SelectMany(m => m.Polygons ?? []).Where(c => c.Type is >= 64 and <= 66))
                    {
                        if (!NjStrip.Write(chunk.Type, chunk.HeadBits, chunk.Strips()).Body.AsSpan().SequenceEqual(chunk.Body))
                            failures.Add($"{where}: strip chunk {chunk.Type} does not write back");
                        strips++;
                    }
                }
                catch (InvalidDataException ex)
                {
                    failures.Add($"{where}: {ex.Message}");
                }
                foreach (var t in e.Textures)
                {
                    var group = layout.TextureGroups[t];
                    try
                    {
                        if (!TextureEntry.Read(group.Span).ToBytes().AsSpan().SequenceEqual(group.Span))
                            failures.Add($"{where}: texture entry {t} differs");
                    }
                    catch (InvalidDataException ex)
                    {
                        failures.Add($"{where}: texture entry {t}: {ex.Message}");
                    }
                    textures++;
                }
            }
        }
        Assert.True(failures.Count == 0, $"{failures.Count} failures of {pages} pages:\n" + string.Join("\n", failures.Take(40)));
        Assert.True(pages > 500 && strips > 1000 && textures > 500, $"{pages} pages, {strips} strip chunks, {textures} texture entries");
    }

    /// <summary>The player models (SYSTEM.AFS 10-14): a size, then a model page like the rooms'.</summary>
    [GameDataFact]
    public void ThePlayerModelsWriteBackByteForByte()
    {
        using var afs = AfsArchive.Open(Path.Combine(TestData.Directory!, "SYSTEM.AFS"));
        foreach (var entry in new[] { 10, 11, 12, 13, 14 })
        {
            var data = afs.ReadEntry(entry);
            var size = BinaryPrimitives.ReadInt32LittleEndian(data);
            var bytes = data.AsSpan(4, size);
            var page = ModelPage.Read(bytes);
            Assert.True(page.ToBytes().AsSpan().SequenceEqual(bytes), $"entry {entry}");
            Assert.NotEmpty(page.Models);
        }
    }

    private static int FirstDifference(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++)
        {
            if (a[i] != b[i])
                return i;
        }
        return n;
    }
}
