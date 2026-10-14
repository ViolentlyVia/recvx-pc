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
}
