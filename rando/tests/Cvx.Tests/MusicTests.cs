using System.Buffers.Binary;
using System.IO.Compression;
using Cvx.Formats;
using Cvx.Rando;
using Cvx.Rando.Data;
using Cvx.Rando.Music;

namespace Cvx.Tests;

public class MusicTests
{
    private static readonly RandoData Data = RandoData.Load();

    [GameDataFact]
    public void ScriptsOnlyAskForTracksTheirDiscHas()
    {
        // Rooms of stages 0-5 play from BGM1.AFS, stages 6-9 from BGM2.AFS: the rule the shuffle
        // relies on when it keeps swaps to tracks of the same discs.
        var data = TestData.Open();
        var discs = MusicRandomizer.Present(data);
        using var rooms = RoomArchive.Open(data);
        var bad = RoomTable.All
            .SelectMany(id => Music.Requests(id, rooms.ReadRoom(id)))
            .Where(r => r.Track != 0xFF && !discs[r.Room.Stage >= 6 ? 1 : 0].Contains(r.Track))
            .Select(r => $"{r.Room} {r.Opcode} {r.Track}")
            .ToList();
        Assert.Empty(bad);
    }

    [GameDataFact]
    public void SwapsStayInCategoryAndDisc()
    {
        var discs = MusicRandomizer.Present(TestData.Open());
        foreach (var seed in Enumerable.Range(1, 30))
        {
            var swaps = MusicRandomizer.Shuffle(Data.Music, discs, Rng.For(seed, "music"));
            foreach (var group in swaps.GroupBy(s => s.Category))
            {
                var tracks = Data.Music.Categories.Single(c => c.Name == group.Key).Tracks;
                Assert.All(group, s => Assert.Contains(s.To, tracks));
                Assert.Equal(group.Count(), group.Select(s => s.To).Distinct().Count());   // a permutation
            }
            Assert.All(swaps, s => Assert.True(discs.All(d => !d.Contains(s.From) || d.Contains(s.To)), $"seed {seed}: {s}"));
        }
    }

    [Fact]
    public void EveryCategorisedTrackIsListedOnce()
    {
        var all = Data.Music.Categories.SelectMany(c => c.Tracks).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    // ------------------------------------------------------------------ other games' music

    [Fact]
    public void AdxRoundTripsCloseToTheSource()
    {
        // a chord with a slow swell, as the game's decoder would read it back
        var n = 44100 * 2;
        var left = new short[n];
        var right = new short[n];
        for (var i = 0; i < n; i++)
        {
            var t = i / 44100.0;
            var env = 0.3 + 0.7 * Math.Sin(Math.PI * t / 2);
            left[i] = (short)(9000 * env * (Math.Sin(2 * Math.PI * 220 * t) + 0.5 * Math.Sin(2 * Math.PI * 330 * t)));
            right[i] = (short)(9000 * env * Math.Sin(2 * Math.PI * 277 * t));
        }
        var adx = Adx.Encode([left, right], 44100);

        var info = AdxInfo.Read(adx)!.Value;
        Assert.Equal((2, 44100, n, true), (info.Channels, info.SampleRate, info.Samples, info.Loops));
        Assert.Equal(0, adx.Length % 2048);
        // loop: the whole track, from a sector-aligned start
        Assert.Equal(Adx.DataStart, BinaryPrimitives.ReadInt32BigEndian(adx.AsSpan(0x20)));
        Assert.Equal(n, BinaryPrimitives.ReadInt32BigEndian(adx.AsSpan(0x24)));

        var (pcm, rate) = Adx.Decode(adx);
        Assert.Equal(44100, rate);
        foreach (var (source, decoded) in new[] { (left, pcm[0]), (right, pcm[1]) })
        {
            double signal = 0, noise = 0;
            for (var i = 0; i < n; i++)
            {
                signal += (double)source[i] * source[i];
                noise += Math.Pow(source[i] - decoded[i], 2);
            }
            Assert.True(10 * Math.Log10(signal / noise) > 30, $"SNR {10 * Math.Log10(signal / noise):0.0} dB");
        }
    }

    [Fact]
    public void AOneShotAdxDoesNotLoop()
    {
        // the title call plays once
        var pcm = new short[4410];
        var adx = Adx.Encode([pcm, pcm], 22050, loop: false);
        Assert.False(AdxInfo.Read(adx)!.Value.Loops);
        Assert.Equal(0, BinaryPrimitives.ReadInt16BigEndian(adx.AsSpan(0x16)));   // no loop for ADX_DecodeInfoExLoop
    }

    [GameDataFact]
    public void TheGamesOwnTracksDecode()
    {
        // the decoder reads the game's tracks: plausible levels, not noise or silence
        using var afs = AfsArchive.Open(Path.Combine(TestData.Open().Directory, "BGM1.AFS"));
        var (pcm, rate) = Adx.Decode(afs.ReadEntry(3));
        var (rms, peak) = MusicLibrary.Level(pcm);
        Assert.Equal(48000, rate);
        Assert.InRange(rms, 1000, 12000);
        Assert.InRange(peak, 10000, 32768);
    }

    private static List<LibraryTrack> FakeImports() =>
    [
        .. Enumerable.Range(0, 40).Select(i => new LibraryTrack("re2", i % 2 == 0 ? "calm" : "creepy", $"area{i}", "x.zip", "e")),
        .. Enumerable.Range(0, 10).Select(i => new LibraryTrack("re2", "danger", $"danger{i}", "x.zip", "e")),
        .. Enumerable.Range(0, 3).Select(i => new LibraryTrack("sh1", "boss", $"boss{i}", "x.zip", "e")),
        new LibraryTrack("sh1", "sting", "sting", "x.zip", "e"),
    ];

    /// <summary>Discs like the game's: tracks on both, on one only, and free entries.</summary>
    private static IReadOnlyList<ISet<int>> FakeDiscs()
    {
        var all = Data.Music.Categories.SelectMany(c => c.Tracks).Order().ToList();
        var one = all.Where((_, i) => i % 3 != 2).ToHashSet();
        var two = all.Where((_, i) => i % 3 != 1).ToHashSet();
        return [one, two];
    }

    [Fact]
    public void WithoutOtherGamesTheMixIsTheShuffle()
    {
        // seeds made before other games' music keep their music
        var discs = FakeDiscs();
        foreach (var seed in Enumerable.Range(1, 10))
        {
            Assert.Equal(MusicRandomizer.Shuffle(Data.Music, discs, Rng.For(seed, "music")),
                MusicRandomizer.Mix(Data.Music, discs, [], ownMusic: true, Rng.For(seed, "music")));
        }
    }

    [Fact]
    public void OtherGamesTracksGoToTheirKindAndFreeEntries()
    {
        var discs = FakeDiscs();
        var tags = Data.Music.Categories.ToDictionary(c => c.Name, c => c.BioRand.ToHashSet());
        foreach (var seed in Enumerable.Range(1, 20))
        {
            var own = seed % 2 == 0;
            var swaps = MusicRandomizer.Mix(Data.Music, discs, FakeImports(), own, Rng.For(seed, "music"));
            Assert.Equal(Data.Music.Categories.Sum(c => c.Tracks.Length), swaps.Count);
            Assert.Contains(swaps, s => s.Import != null);
            Assert.DoesNotContain(swaps, s => s.Import?.Tag == "sting");
            foreach (var s in swaps.Where(s => s.Import != null))
            {
                Assert.Contains(s.Import!.Tag, tags[s.Category]);
                // the entry is free on every disc the replaced track plays on, and below the partition's limit
                var on = Enumerable.Range(0, 2).Where(d => discs[d].Contains(s.From)).Select(d => d + 1).ToArray();
                Assert.Equal(on, s.Discs);
                Assert.All(on, d => Assert.DoesNotContain(s.To, discs[d - 1]));
                Assert.InRange(s.To, 0, MusicRandomizer.MaxTracks - 1);
            }
            // an entry holds one track on a disc
            foreach (var disc in new[] { 1, 2 })
            {
                var entries = swaps.Where(s => s.Import != null && s.Discs!.Contains(disc)).GroupBy(s => s.To);
                Assert.All(entries, g => Assert.Single(g.Select(s => s.Import!.Key).Distinct()));
            }
            if (!own)
            {
                // without the game's own music, every kind the chosen games have is theirs
                Assert.All(swaps.Where(s => s.Category is "Area" or "Tension" or "Boss"), s => Assert.NotNull(s.Import));
                Assert.All(swaps.Where(s => s.Category == "Save room"), s => Assert.Equal(s.From, s.To));   // no "safe" tracks here
            }
        }
    }

    [Fact]
    public void TheLibraryReadsBioRandsDatapacks()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cvx-biorand-" + Guid.NewGuid().ToString("N"), "biorand");
        Directory.CreateDirectory(Path.Combine(dir, "datapacks"));
        try
        {
            using (var zip = ZipFile.Open(Path.Combine(dir, "datapacks", "classic.zip"), ZipArchiveMode.Create))
            {
                foreach (var name in new[] { "data/bgm/re2/calm/main03.ogg", "data/bgm/re2/danger/main09.ogg", "data/bgm/recv/calm/recv-calm1.ogg",
                             "data/bgm/re2/", "data/hurt/claire.re2/0.ogg", "LICENSE",
                             "data/title/barb.ogg", "data/title/jj.ogg", "data/title/template.ogg" })
                    zip.CreateEntry(name);
            }
            // found as a "biorand" folder next to where the program is
            var library = MusicLibrary.Find(null, Path.GetDirectoryName(dir))!;
            Assert.Equal(dir, library.Directory);
            Assert.Equal(["re2/calm/main03", "re2/danger/main09"], library.Tracks.Select(t => t.Key));
            Assert.Equal(["re2"], library.Games);
            // the title calls, without the template (the music with no voice)
            Assert.Equal(["barb", "jj"], library.TitleCalls.Select(t => t.Name));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);
        }
    }
}
