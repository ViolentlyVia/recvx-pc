using Cvx.Formats;

namespace Cvx.Tests;

public class SoundTests
{
    [GameDataFact]
    public void EveryRoomSoundPackRebuildsTheSame()
    {
        var bad = new List<string>();
        foreach (var disc in new[] { 1, 2 })
        {
            using var packs = SoundPacks.Open(TestData.Open(), disc);
            Assert.True(packs.Keys.Count > 50);
            for (var i = 0; i < packs.Keys.Count; i++)
            {
                var pack = packs.Afs.ReadEntry(i);
                if (!SoundPacks.Build(SoundPacks.Parse(pack)).AsSpan().SequenceEqual(pack))
                    bad.Add($"{packs.Name} #{i} (key {packs.Keys[i]})");
            }
        }
        Assert.Empty(bad);
    }
}
