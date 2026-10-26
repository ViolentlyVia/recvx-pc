using Cvx.Formats;
using Cvx.Formats.Models;
using Cvx.Rando;
using Cvx.Rando.Data;
using Cvx.Rando.Title;

namespace Cvx.Tests;

public class TitleTests
{
    [GameDataFact]
    public void TheTitleScreenCarriesTheVersion()
    {
        // ADV.AFS entry 2's title picture becomes the art, with the caption at the top left;
        // nothing else in the entry changes
        using var adv = AfsArchive.Open(Path.Combine(TestData.Directory!, TitleScreen.Archive));
        var entry = adv.ReadEntry(TitleScreen.Entry);
        var art = new byte[1024 * 512 * 4];
        for (var i = 0; i < 1024 * 512; i++)
            (art[i * 4], art[i * 4 + 1], art[i * 4 + 2], art[i * 4 + 3]) = ((byte)(i % 1024 / 64 * 16), 20, (byte)(i / 1024 / 64 * 32), 255);   // 128 colours, as art of 256 or fewer
        var made = TitleScreen.Make(entry, Png.Encode(1024, 512, art), "Biorand++ 0.9.94 (b6da6e9)");

        Assert.Equal(entry.Length, made.Length);
        Assert.True(made.AsSpan(0, 0x460 + 8).SequenceEqual(entry.AsSpan(0, 0x460 + 8)));   // the palette, the TIM2's header and global index
        Assert.True(made.AsSpan(0x80960).SequenceEqual(entry.AsSpan(0x80960)));
        var picture = new Tim2Picture(made[0x460..0x80960]);
        Assert.Equal((1024, 512, 5), (picture.Width, picture.Height, picture.ImageType));
        var rgba = picture.ToRgba();
        uint At(int x, int y) => BitConverter.ToUInt32(rgba, (y * 1024 + x) * 4);

        // the art where nothing is drawn (the palette keeps its colours)
        Assert.Equal(BitConverter.ToUInt32(art, (300 * 1024 + 300) * 4), At(300, 300));
        // the caption: white dots in the top left corner
        Assert.Contains(Enumerable.Range(16, 300).SelectMany(x => Enumerable.Range(16, 21).Select(y => At(x, y))), c => c == 0xFFFFFFFF);
        // nothing else drawn: the screen's top right is the art (BioRand's QR code is left out)
        for (var y = 0; y < 200; y += 3)
        {
            for (var x = 500; x < 640; x += 3)
                Assert.Equal(BitConverter.ToUInt32(art, (y * 1024 + x) * 4), At(x, y));
        }
    }

    [BioRandFact]
    public void SeedsGetTheTitleScreenWithBioRandsArt()
    {
        // BioRand's CV:X art (data/recv/bg.png): every seed's title, its spoiler says so
        Assert.NotNull(TitleScreen.FindBackground(BioRandData.Directory));
        using var rooms = RoomArchive.Open(TestData.Open());
        var library = Cvx.Rando.Music.MusicLibrary.Open(BioRandData.Directory);
        var result = Generator.Generate(new RandoSettings { Seed = 4, RandomItems = false }, RandoData.Load(), rooms, null, library);
        Assert.NotNull(result.TitleBackground);
        Assert.Contains("Title screen: BioRand's, with Biorand++", result.Spoiler);
        var picture = Generator.TitlePicture(rooms.Data!, result);
        Assert.NotNull(picture);
        Assert.StartsWith("Biorand++ ", TitleScreen.Caption);
    }
}
