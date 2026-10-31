using Cvx.Formats;
using Cvx.Formats.Models;
using Cvx.Rando.Npcs;

namespace Cvx.Tests;

public class StatusTests
{
    [GameDataFact]
    public void TheStatusScreenShowsTheNewLook()
    {
        // Claire as a BioRand character: her face, name plate and data card change on the page;
        // nothing else of the entry does, and the rest of the page keeps its pixels and colours
        using var afs = AfsArchive.Open(Path.Combine(TestData.Directory!, StatusScreen.Archives[0]));
        var entry = afs.ReadEntry(StatusScreen.Entry);
        var face = new byte[40 * 56 * 4];
        for (var i = 0; i < 40 * 56; i++)
            (face[i * 4], face[i * 4 + 1], face[i * 4 + 2], face[i * 4 + 3]) = i / 40 is >= 20 and < 26 ? ((byte)0, (byte)0, (byte)0, (byte)255) : ((byte)(i % 40 * 6), (byte)(i / 40 * 4), (byte)90, (byte)255);   // and a black band
        var made = StatusScreen.Make(entry, new Dictionary<int, StatusLook> { [0] = new("KENDO", Png.Encode(40, 56, face)) });

        Assert.Equal(entry.Length, made.Length);
        var (before, after) = (TextureEntry.Read(entry), TextureEntry.Read(made));
        Assert.Equal(before.Records.Count, after.Records.Count);
        var page = before.Records.Select((r, i) => (r, i)).Where(x => x.r.Code == TextureRecord.Tim2Code).ElementAt(StatusScreen.Page).i;
        foreach (var (r, i) in after.Records.Select((r, i) => (r, i)).Where(x => x.i != page))
            Assert.True(r.Data.AsSpan().SequenceEqual(before.Records[i].Data));

        var (old, now) = (new Tim2Picture(before.Records[page].Data), new Tim2Picture(after.Records[page].Data));
        Assert.Equal((512, 512, 5, old.GlobalIndex), (now.Width, now.Height, now.ImageType, now.GlobalIndex));
        bool Claire(int x, int y) => (x < 88 && y < 88) || (x >= 352 && x < 440 && y < 20) || (x < 140 && y >= 88 && y < 148);
        var (oldPalette, newPalette) = (old.Palette(), now.Palette());
        var (oldImage, newImage) = (old.Image.ToArray(), now.Image.ToArray());
        int changed = 0;
        for (var y = 0; y < 512; y++)
        {
            for (var x = 0; x < 512; x++)
            {
                var i = y * 512 + x;
                if (Claire(x, y))
                    changed += oldPalette[oldImage[i]] != newPalette[newImage[i]] ? 1 : 0;
                else
                    Assert.Equal(oldPalette[oldImage[i]], newPalette[newImage[i]]);
            }
        }
        Assert.True(changed > 88 * 88 / 2, $"{changed} pixels changed");
        // her new face is opaque (the page's see-through colours are left to what used them)
        Assert.All(Enumerable.Range(0, 88).SelectMany(y => Enumerable.Range(0, 88).Select(x => newPalette[newImage[y * 512 + x]])), c => Assert.True(c >> 24 >= 0x80));
        // her card: the name's line written, the height, weight and blood type lines blank
        var card = Enumerable.Range(88 + 24, 20).SelectMany(y => Enumerable.Range(8, 120).Select(x => newPalette[newImage[y * 512 + x]])).Distinct();
        Assert.Single(card);
    }

    [Fact]
    public void LooksShowTheirName() =>
        Assert.Equal(("KENDO", "DIMITRESCU"), (StatusScreen.NameOf("kendo.re2"), StatusScreen.NameOf("dimitrescu.re8")));
}
