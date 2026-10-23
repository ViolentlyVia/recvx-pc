using Cvx.Formats;
using Cvx.Rando.Data;

namespace Cvx.Rando.Music;

/// <summary>
/// One track swap: when a room asks for <see cref="From"/>, <see cref="To"/> plays. With
/// <see cref="Import"/>, <see cref="To"/> is a free entry of the music archives that gets that
/// track of another game (<see cref="Discs"/>: the archives it goes in, 1 and/or 2).
/// </summary>
public sealed record MusicSwap(string Category, int From, int To, LibraryTrack? Import = null, int[]? Discs = null);

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

    /// <summary>How many entries the game's BGM partition can hold (sdfunc.c SoundAfsPatDef).</summary>
    public const int MaxTracks = 128;

    /// <summary>
    /// Music from this game and others: each category's tracks become ones picked from a pool of
    /// the chosen games' tracks of that kind (this game's own when <paramref name="ownMusic"/>,
    /// and <paramref name="imports"/>, BioRand's tracks of the chosen games). Every track of the
    /// pool is used once before any is used again. An imported track gets an archive entry that
    /// is free on every disc the replaced track plays on. Without imports this is
    /// <see cref="Shuffle"/>.
    /// </summary>
    public static List<MusicSwap> Mix(MusicCatalog catalog, IReadOnlyList<ISet<int>> discs, IReadOnlyList<LibraryTrack> imports,
        bool ownMusic, Rng rng)
    {
        if (imports.Count == 0)
            return ownMusic ? Shuffle(catalog, discs, rng) : [];

        var used = discs.Select(d => new HashSet<int>(d)).ToList();   // entries taken, per disc
        var entries = new Dictionary<(string, string), int>();          // an import already given an entry, by discs
        var swaps = new List<MusicSwap>();
        foreach (var category in catalog.Categories)
        {
            var slots = category.Tracks.Where(t => discs.Any(d => d.Contains(t))).Order().ToList();
            var tags = category.BioRand.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var pool = new List<object>();
            if (ownMusic)
                pool.AddRange(slots.Cast<object>());
            pool.AddRange(imports.Where(t => tags.Contains(t.Tag)));
            if (pool.Count == 0)
            {
                // nothing of this kind in the chosen games: the game's own stay
                swaps.AddRange(slots.Select(t => new MusicSwap(category.Name, t, t)));
                continue;
            }

            var bag = new List<object>();
            foreach (var from in rng.Shuffle(slots))
            {
                var on = Enumerable.Range(0, discs.Count).Where(d => discs[d].Contains(from)).ToArray();
                bool Fits(object o) => o is not int t || on.All(d => discs[d].Contains(t));
                if (!bag.Any(Fits))
                    bag.AddRange(rng.Shuffle(pool));
                var pick = bag.FirstOrDefault(Fits);
                if (pick == null)
                {
                    swaps.Add(new MusicSwap(category.Name, from, from));
                    continue;
                }
                bag.Remove(pick);
                if (pick is int own)
                {
                    swaps.Add(new MusicSwap(category.Name, from, own));
                    continue;
                }
                var track = (LibraryTrack)pick;
                var key = (track.Key, string.Join(",", on));
                if (!entries.TryGetValue(key, out var entry))
                {
                    entry = Enumerable.Range(0, MaxTracks).FirstOrDefault(i => on.All(d => !used[d].Contains(i)), -1);
                    if (entry < 0)
                    {
                        // the archives are full: this one keeps its own track
                        swaps.Add(new MusicSwap(category.Name, from, from));
                        continue;
                    }
                    foreach (var d in on)
                        used[d].Add(entry);
                    entries[key] = entry;
                }
                swaps.Add(new MusicSwap(category.Name, from, entry, track, on.Select(d => d + 1).ToArray()));
            }
        }
        return swaps.OrderBy(s => catalog.Categories.FindIndex(c => c.Name == s.Category)).ThenBy(s => s.From).ToList();
    }

    /// <summary>The tracks each disc has: BGM1.AFS and BGM2.AFS entries that are not placeholders.</summary>
    public static IReadOnlyList<ISet<int>> Present(GameData data) =>
        new[] { 1, 2 }.Select(disc =>
        {
            using var afs = AfsArchive.Open(Path.Combine(data.Directory, $"BGM{disc}.AFS"));
            return (ISet<int>)afs.Entries.Where(e => e.Size > Formats.Music.PlaceholderSize).Select(e => e.Index).ToHashSet();
        }).ToList();
}
