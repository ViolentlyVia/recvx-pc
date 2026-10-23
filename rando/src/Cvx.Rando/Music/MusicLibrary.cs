using System.IO.Compression;
using NVorbis;

namespace Cvx.Rando.Music;

/// <summary>A track from BioRand's music: data/bgm/&lt;game&gt;/&lt;tag&gt;/&lt;name&gt;.ogg in a datapack zip or a folder.</summary>
/// <param name="Source">The zip (or, for loose files, the file itself).</param>
/// <param name="Entry">The path inside the zip; null for a loose file.</param>
public sealed record LibraryTrack(string Game, string Tag, string Name, string Source, string? Entry)
{
    public string Key => $"{Game}/{Tag}/{Name}";
    public override string ToString() => Key;
}

/// <summary>
/// The music of other games that BioRand brings (its datapacks: classic.zip, modern.zip...).
/// Only read from the player's own BioRand folder; nothing of it is part of this program.
/// </summary>
public sealed class MusicLibrary
{
    /// <summary>This game's own music: always from its BGM archives, never BioRand's copy of it.</summary>
    public const string OwnGame = "recv";

    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["recv"] = "Code: Veronica",
        ["re0"] = "RE0",
        ["re0_n64"] = "RE0 (N64)",
        ["re1"] = "RE1",
        ["re1_dc"] = "RE1 Director's Cut",
        ["re1.5"] = "RE1.5",
        ["re1r"] = "RE1 Remake",
        ["re2"] = "RE2",
        ["re2r"] = "RE2 Remake",
        ["re3"] = "RE3",
        ["re3r"] = "RE3 Remake",
        ["re4"] = "RE4",
        ["re4r"] = "RE4 Remake",
        ["re5"] = "RE5",
        ["re6"] = "RE6",
        ["re7"] = "RE7",
        ["re7_jack"] = "RE7 (Jack)",
        ["re8"] = "Village",
        ["redc"] = "Darkside Chronicles",
        ["reuc"] = "Umbrella Chronicles",
        ["resurv"] = "Survivor",
        ["reout"] = "Outbreak",
        ["rerev1"] = "Revelations",
        ["rerev2"] = "Revelations 2",
        ["dc1"] = "Dino Crisis",
        ["sh1"] = "Silent Hill",
    };

    public required string Directory { get; init; }
    /// <summary>Every track, by game, tag and name (a stable order: the same seed picks the same tracks).</summary>
    public required IReadOnlyList<LibraryTrack> Tracks { get; init; }
    /// <summary>
    /// BioRand's title calls (data/title/*.ogg: the series' name, recorded by people of its
    /// community), by name. Its template (the music without a voice) is left out.
    /// </summary>
    public IReadOnlyList<LibraryTrack> TitleCalls { get; init; } = [];

    /// <summary>The games with music, other than this one.</summary>
    public IReadOnlyList<string> Games => Tracks.Select(t => t.Game).Distinct().ToList();

    public static string DisplayName(string game) => Names.TryGetValue(game, out var name) ? name : game.ToUpperInvariant();

    /// <summary>The music in a BioRand folder (its datapacks and data\bgm); null when it has none.</summary>
    public static MusicLibrary? Open(string? directory)
    {
        if (string.IsNullOrEmpty(directory) || !System.IO.Directory.Exists(directory))
            return null;
        var tracks = new Dictionary<string, LibraryTrack>(StringComparer.OrdinalIgnoreCase);
        var titles = new Dictionary<string, LibraryTrack>(StringComparer.OrdinalIgnoreCase);
        void Add(string path, string source, string? entry)
        {
            // data/title/<name>.ogg
            var p = path.Replace('\\', '/').Split('/');
            if (p.Length >= 3 && p[^3].Equals("data", StringComparison.OrdinalIgnoreCase) && p[^2].Equals("title", StringComparison.OrdinalIgnoreCase) &&
                p[^1].EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
            {
                var name = Path.GetFileNameWithoutExtension(p[^1]);
                if (!name.Equals("template", StringComparison.OrdinalIgnoreCase))
                    titles.TryAdd(name, new LibraryTrack("title", "title", name, source, entry));
                return;
            }
            // data/bgm/<game>/<tag>/<name>.ogg
            var parts = path.Replace('\\', '/').Split('/');
            var at = Array.FindIndex(parts, p => p.Equals("bgm", StringComparison.OrdinalIgnoreCase));
            if (at < 1 || !parts[at - 1].Equals("data", StringComparison.OrdinalIgnoreCase) || parts.Length != at + 4 ||
                !parts[^1].EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
                return;
            var game = parts[at + 1].ToLowerInvariant();
            if (game == OwnGame)
                return;
            var track = new LibraryTrack(game, parts[at + 2].ToLowerInvariant(), Path.GetFileNameWithoutExtension(parts[^1]), source, entry);
            tracks.TryAdd(track.Key, track);   // a folder or an earlier pack wins
        }

        foreach (var loose in new[] { "bgm", "title" })
        {
            var folder = Path.Combine(directory, "data", loose);
            if (!System.IO.Directory.Exists(folder))
                continue;
            foreach (var file in System.IO.Directory.EnumerateFiles(folder, "*.ogg", SearchOption.AllDirectories))
                Add(Path.GetRelativePath(directory, file), file, null);
        }
        var packs = Path.Combine(directory, "datapacks");
        if (System.IO.Directory.Exists(packs))
        {
            foreach (var zip in System.IO.Directory.EnumerateFiles(packs, "*.zip").Order(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    using var archive = ZipFile.OpenRead(zip);
                    foreach (var e in archive.Entries)
                        Add(e.FullName, zip, e.FullName);
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    // a broken or locked pack: the others still count
                }
            }
        }
        if (tracks.Count == 0 && titles.Count == 0)
            return null;
        return new MusicLibrary
        {
            Directory = directory,
            Tracks = tracks.Values.OrderBy(t => t.Game, StringComparer.Ordinal).ThenBy(t => t.Tag, StringComparer.Ordinal)
                .ThenBy(t => t.Name, StringComparer.Ordinal).ToList(),
            TitleCalls = titles.Values.OrderBy(t => t.Name, StringComparer.Ordinal).ToList(),
        };
    }

    /// <summary>The first folder that has BioRand's music: the given one, then a "biorand" folder next to or above each start.</summary>
    public static MusicLibrary? Find(string? remembered, params string?[] starts)
    {
        if (Open(remembered) is { } chosen)
            return chosen;
        foreach (var start in starts)
        {
            for (var dir = string.IsNullOrEmpty(start) ? null : new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                foreach (var name in new[] { "biorand", "BioRand" })
                {
                    if (Open(Path.Combine(dir.FullName, name)) is { } found)
                        return found;
                }
            }
        }
        return null;
    }

    public static byte[] ReadOgg(LibraryTrack track)
    {
        if (track.Entry == null)
            return File.ReadAllBytes(track.Source);
        using var archive = ZipFile.OpenRead(track.Source);
        var entry = archive.GetEntry(track.Entry) ?? throw new FileNotFoundException($"{track.Entry} is not in {track.Source}.");
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Decodes an OGG to 16-bit stereo PCM (mono is doubled, more channels are mixed down).</summary>
    public static (short[][] Channels, int SampleRate) DecodeOgg(byte[] ogg, double gain = 1)
    {
        using var reader = new VorbisReader(new MemoryStream(ogg), closeOnDispose: true);
        var nch = reader.Channels;
        var left = new List<short>();
        var right = new List<short>();
        var buffer = new float[4096 * nch];
        int read;
        while ((read = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i + nch <= read; i += nch)
            {
                float l, r;
                if (nch == 1)
                {
                    l = r = buffer[i];
                }
                else
                {
                    l = buffer[i];
                    r = buffer[i + 1];
                    // more than two: fold the others into both sides
                    for (var c = 2; c < nch; c++)
                    {
                        l += buffer[i + c] * 0.5f;
                        r += buffer[i + c] * 0.5f;
                    }
                }
                left.Add(ToShort(l * gain));
                right.Add(ToShort(r * gain));
            }
        }
        return ([left.ToArray(), right.ToArray()], reader.SampleRate);
    }

    private static short ToShort(double v) => (short)Math.Clamp(Math.Round(v * 32767), short.MinValue, short.MaxValue);

    /// <summary>The RMS level of PCM (all channels together) and its peak, in 16-bit units.</summary>
    public static (double Rms, int Peak) Level(IReadOnlyList<short[]> channels)
    {
        double sum = 0;
        long n = 0;
        var peak = 0;
        foreach (var ch in channels)
        {
            foreach (var s in ch)
            {
                sum += (double)s * s;
                peak = Math.Max(peak, Math.Abs((int)s));
            }
            n += ch.Length;
        }
        return (n == 0 ? 0 : Math.Sqrt(sum / n), peak);
    }

    /// <summary>
    /// The gain that brings a track to <paramref name="targetRms"/> without clipping its peak.
    /// </summary>
    public static double MatchGain(double rms, int peak, double targetRms)
    {
        if (rms <= 0 || targetRms <= 0)
            return 1;
        var gain = targetRms / rms;
        if (peak > 0)
            gain = Math.Min(gain, 32000.0 / peak);
        return gain;
    }
}
