using Cvx.Formats;

namespace Cvx.Tests;

public class SoundTests
{
    [Fact]
    public void VagEncodesWhatTheSpuDecodes()
    {
        // a falling tone with noise, as a scream is; decoded as the SPU (and pc_iop_tsnddrv.c) does
        var rng = new Random(1);
        var pcm = Enumerable.Range(0, 5000).Select(i =>
            (short)(Math.Sin(i * (0.2 - i * 0.00002)) * 12000 * Math.Exp(-i / 4000.0) + rng.Next(-500, 500))).ToArray();
        var data = Vag.Encode(pcm);
        Assert.Equal(0, data.Length % Vag.BlockSize);
        Assert.True(data.AsSpan(0, Vag.BlockSize).IndexOfAnyExcept((byte)0) < 0, "the first block is silent");
        Assert.Equal(1, data[data.Length - Vag.BlockSize + 1] & 1);
        Assert.All(Enumerable.Range(0, data.Length / Vag.BlockSize - 1), b => Assert.Equal(0, data[b * Vag.BlockSize + 1] & 1));

        var back = Vag.Decode(data).Skip(Vag.SamplesPerBlock).Take(pcm.Length).ToArray();
        Assert.Equal(pcm.Length, back.Length);
        double signal = 0, noise = 0;
        for (var i = 0; i < pcm.Length; i++)
        {
            signal += (double)pcm[i] * pcm[i];
            noise += (double)(pcm[i] - back[i]) * (pcm[i] - back[i]);
        }
        Assert.True(10 * Math.Log10(signal / noise) > 20, $"{10 * Math.Log10(signal / noise):0.0} dB");
    }

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
