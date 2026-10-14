using Cvx.Formats;
using Cvx.Rando.Data;

namespace Cvx.Rando.Music;

/// <summary>One track swap: when a room asks for <see cref="From"/>, <see cref="To"/> plays.</summary>
public sealed record MusicSwap(string Category, int From, int To);

/// <summary>
/// Shuffles the music within each category of data/music.json. The swaps go to rando.ini
/// (bgm_map), so no game file changes. A track only becomes one that every disc it is on also
/// has (each disc's archive holds only its own tracks).
/// </summary>
public static class MusicRandomizer
{
    /// <param name="discs">The tracks each disc's archive has (see <see cref="Present"/>).</param>
    public static List<MusicSwap> Shuffle(MusicCatalog catalog, IReadOnlyList<ISet<int>> discs, Rng rng)
    {
        var swaps = new List<MusicSwap>();
        foreach (var category in catalog.Categories)
        {
            var tracks = category.Tracks.Where(t => discs.Any(d => d.Contains(t))).Order().ToList();
            bool Fits(int from, int to) => discs.All(d => !d.Contains(from) || d.Contains(to));

            // A random matching that respects the discs: try a few orders, keep the first that
            // places every track (the most constrained tracks pick first).
            Dictionary<int, int>? best = null;
            for (var attempt = 0; attempt < 50 && best == null; attempt++)
            {
                var free = rng.Shuffle(tracks);
                var map = new Dictionary<int, int>();
                foreach (var from in rng.Shuffle(tracks).OrderBy(f => tracks.Count(t => Fits(f, t))))
                {
                    var to = free.Where(t => Fits(from, t)).ToList();
                    if (to.Count == 0)
                        break;
                    var pick = rng.NextOf(to);
                    map[from] = pick;
                    free.Remove(pick);
                }
                if (map.Count == tracks.Count)
                    best = map;
            }
            best ??= tracks.ToDictionary(t => t, t => t);
            swaps.AddRange(best.OrderBy(x => x.Key).Select(x => new MusicSwap(category.Name, x.Key, x.Value)));
        }
        return swaps;
    }

    /// <summary>The tracks each disc has: BGM1.AFS and BGM2.AFS entries that are not placeholders.</summary>
    public static IReadOnlyList<ISet<int>> Present(GameData data) =>
        new[] { 1, 2 }.Select(disc =>
        {
            using var afs = AfsArchive.Open(Path.Combine(data.Directory, $"BGM{disc}.AFS"));
            return (ISet<int>)afs.Entries.Where(e => e.Size > Formats.Music.PlaceholderSize).Select(e => e.Index).ToHashSet();
        }).ToList();
}
