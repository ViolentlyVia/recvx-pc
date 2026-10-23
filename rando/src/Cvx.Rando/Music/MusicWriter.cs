using System.Collections.Concurrent;
using Cvx.Formats;

namespace Cvx.Rando.Music;

/// <summary>
/// Puts other games' music into the mod folder: their tracks converted to ADX in BGM1.AFS and
/// BGM2.AFS at the entries <see cref="MusicRandomizer.Mix"/> gave them (rando.ini's bgm_map
/// points there). The game's own tracks stay where they are.
/// </summary>
public static class MusicWriter
{
    public static void Write(GameData data, double targetRms, IReadOnlyList<MusicSwap> swaps, string directory, IProgress<string>? progress = null)
    {
        var imports = swaps.Where(s => s.Import != null).ToList();
        var tracks = imports.Select(s => s.Import!).DistinctBy(t => t.Key).ToList();
        var converted = new ConcurrentDictionary<string, byte[]>();
        if (tracks.Count != 0)
        {
            var done = 0;
            progress?.Report($"Converting {tracks.Count} music tracks...");
            Parallel.ForEach(tracks, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, track =>
            {
                converted[track.Key] = Convert(track, targetRms);
                var n = Interlocked.Increment(ref done);
                progress?.Report($"Converting music tracks... {n}/{tracks.Count}");
            });
        }

        foreach (var disc in new[] { 1, 2 })
        {
            var name = $"BGM{disc}.AFS";
            var path = Path.Combine(directory, name);
            var replacements = new Dictionary<int, byte[]>();
            foreach (var s in imports.Where(s => s.Discs!.Contains(disc)))
                replacements[s.To] = converted[s.Import!.Key];
            if (replacements.Count == 0)
            {
                if (File.Exists(path))
                    File.Delete(path);
                continue;
            }
            progress?.Report($"Writing {name}...");
            using var afs = AfsArchive.Open(Path.Combine(data.Directory, name));
            // entries added past the end get the archive's own placeholder (a 72-byte silent ADX)
            var placeholder = afs.Entries.First(e => e.Size <= Formats.Music.PlaceholderSize);
            AfsWriter.Write(afs, replacements, path, afs.ReadEntry(placeholder.Index));
        }
    }

    /// <summary>ADV.AFS's entry that TitleCall (adv.c) plays when a title menu item is picked.</summary>
    public const int TitleCallEntry = 3;
    public const string TitleArchive = "ADV.AFS";

    /// <summary>
    /// The title call: ADV.AFS with <paramref name="title"/> in place of the game's (as loud as
    /// the game's), or no ADV.AFS in the mod folder when there is none.
    /// </summary>
    public static void WriteTitleCall(GameData data, LibraryTrack? title, string directory, IProgress<string>? progress = null)
    {
        var path = Path.Combine(directory, TitleArchive);
        if (title == null)
        {
            if (File.Exists(path))
                File.Delete(path);
            return;
        }
        progress?.Report("Writing the title call...");
        using var afs = AfsArchive.Open(Path.Combine(data.Directory, TitleArchive));
        var (own, _) = Adx.Decode(afs.ReadEntry(TitleCallEntry));
        AfsWriter.Write(afs, new Dictionary<int, byte[]> { [TitleCallEntry] = Convert(title, MusicLibrary.Level(own).Rms, loop: false) }, path);
    }

    /// <summary>A BioRand track as ADX: decoded, brought to the game's loudness, looping over the whole track (or once).</summary>
    public static byte[] Convert(LibraryTrack track, double targetRms, bool loop = true)
    {
        var (pcm, rate) = MusicLibrary.DecodeOgg(MusicLibrary.ReadOgg(track));
        var (rms, peak) = MusicLibrary.Level(pcm);
        var gain = MusicLibrary.MatchGain(rms, peak, targetRms);
        if (Math.Abs(gain - 1) > 0.01)
        {
            foreach (var ch in pcm)
            {
                for (var i = 0; i < ch.Length; i++)
                    ch[i] = (short)Math.Clamp(Math.Round(ch[i] * gain), short.MinValue, short.MaxValue);
            }
        }
        return Adx.Encode(pcm, rate, loop);
    }
}
