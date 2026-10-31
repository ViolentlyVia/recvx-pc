using System.IO.Compression;
using Cvx.Rando.Music;

namespace Cvx.Rando.Npcs;

/// <summary>
/// One of BioRand's characters in RE2's player model form (<c>data/re2/pld0|pld1/&lt;id&gt;/PL0x.PLD</c>):
/// <see cref="Id"/> is its folder, "&lt;name&gt;.&lt;game&gt;" (e.g. leon.re2).
/// </summary>
/// <param name="Source">The datapack zip, or the PLD file for a loose folder.</param>
/// <param name="Entry">The PLD's entry in the zip; null for a loose file.</param>
public sealed record LibraryCharacter(string Id, string Source, string? Entry)
{
    public string Name => CharacterLibrary.DisplayName(Id);
}

/// <summary>
/// One of a BioRand character's voice clips (<c>data/voice/&lt;character&gt;/&lt;name&gt;.ogg</c>). As in
/// BioRand, the name carries tags: <c>_kind</c> (radio, doom...) and <c>-name</c> conditions (said
/// only with that character there, or not there for <c>-noname</c>).
/// </summary>
/// <param name="Source">The datapack zip, or the OGG file for a loose folder.</param>
/// <param name="Entry">The clip's entry in the zip; null for a loose file.</param>
public sealed record VoiceClip(string Character, string Name, string Source, string? Entry, double Seconds)
{
    /// <summary>A plain line: no kind and no conditions.</summary>
    public bool IsPlain => !Path.GetFileNameWithoutExtension(Name).AsSpan().ContainsAny('_', '-');

    /// <summary>A line of no special kind (it may have conditions).</summary>
    public bool HasNoKind => !Path.GetFileNameWithoutExtension(Name).Contains('_');
}

/// <summary>
/// BioRand's characters that can become NPC looks: from the datapacks (zips) or loose
/// <c>data/re2</c> folders of the player's BioRand folder. Nothing of BioRand ships with the
/// program; the characters are read and converted when a seed is made.
/// </summary>
public sealed class CharacterLibrary
{
    public required string Directory { get; init; }
    /// <summary>By id (a stable order: the same seed picks the same looks).</summary>
    public required IReadOnlyList<LibraryCharacter> Characters { get; init; }

    public LibraryCharacter? Get(string id) => Characters.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>"leon.re2" -> "Leon (RE2)", "chris.re1$battle" -> "Chris (RE1, battle)".</summary>
    public static string DisplayName(string id)
    {
        var dot = id.IndexOf('.');
        if (dot <= 0)
            return id;
        var name = char.ToUpperInvariant(id[0]) + id[1..dot];
        var game = id[(dot + 1)..].Split('$');
        return $"{name} ({MusicLibrary.DisplayName(game[0])}{(game.Length > 1 ? ", " + game[1] : "")})";
    }

    /// <summary>The characters in a BioRand folder; null when it has none.</summary>
    public static CharacterLibrary? Open(string? directory)
    {
        if (string.IsNullOrEmpty(directory) || !System.IO.Directory.Exists(directory))
            return null;
        var found = new Dictionary<string, LibraryCharacter>(StringComparer.OrdinalIgnoreCase);
        void Add(string path, string source, string? entry)
        {
            // data/re2/pld0/<id>/PL00.PLD, data/re2/pld1/<id>/PL01.PLD
            var p = path.Replace('\\', '/').Split('/');
            if (p.Length >= 5 && p[^5].Equals("data", StringComparison.OrdinalIgnoreCase) && p[^4].Equals("re2", StringComparison.OrdinalIgnoreCase) &&
                p[^3] is "pld0" or "pld1" && p[^1].Equals(p[^3] == "pld0" ? "PL00.PLD" : "PL01.PLD", StringComparison.OrdinalIgnoreCase) && p[^2].Contains('.'))
            {
                found.TryAdd(p[^2], new LibraryCharacter(p[^2].ToLowerInvariant(), source, entry));
            }
        }

        var loose = Path.Combine(directory, "data", "re2");
        if (System.IO.Directory.Exists(loose))
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(loose, "PL0?.PLD", SearchOption.AllDirectories))
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
        if (found.Count == 0)
            return null;
        return new CharacterLibrary
        {
            Directory = directory,
            Characters = found.Values.OrderBy(c => c.Id, StringComparer.Ordinal).ToList(),
        };
    }

    /// <summary>The first folder that has BioRand's characters: the given one, then a "biorand" folder next to or above each start.</summary>
    public static CharacterLibrary? Find(string? remembered, params string?[] starts)
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

    /// <summary>
    /// A character's voice clips (<c>data/voice/&lt;id&gt;/*.ogg</c>, the id without a <c>$</c> variant), by
    /// name, with their length: from the loose folder first, then the datapacks.
    /// </summary>
    public IReadOnlyList<VoiceClip> VoiceClips(string id)
    {
        var folder = id.Split('$')[0].ToLowerInvariant();
        var found = new Dictionary<string, VoiceClip>(StringComparer.OrdinalIgnoreCase);
        void Add(string name, string source, string? entry, Func<Stream> open)
        {
            if (!name.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) || found.ContainsKey(name))
                return;
            try
            {
                using var stream = open();
                using var reader = new NVorbis.VorbisReader(stream, closeOnDispose: false);
                found[name] = new VoiceClip(folder, name, source, entry, reader.TotalTime.TotalSeconds);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
            {
                // a clip that does not read is left out
            }
        }

        var loose = Path.Combine(Directory, "data", "voice", folder);
        if (System.IO.Directory.Exists(loose))
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(loose, "*.ogg"))
                Add(Path.GetFileName(file), file, null, () => File.OpenRead(file));
        }
        var packs = Path.Combine(Directory, "datapacks");
        if (System.IO.Directory.Exists(packs))
        {
            var prefix = $"data/voice/{folder}/";
            foreach (var zip in System.IO.Directory.EnumerateFiles(packs, "*.zip").Order(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    using var archive = ZipFile.OpenRead(zip);
                    foreach (var e in archive.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && e.FullName.Length > prefix.Length))
                    {
                        // a zip entry's stream does not seek: read it whole
                        Add(e.Name, zip, e.FullName, () =>
                        {
                            using var s = e.Open();
                            var ms = new MemoryStream();
                            s.CopyTo(ms);
                            ms.Position = 0;
                            return ms;
                        });
                    }
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    // a broken or locked pack: the others still count
                }
            }
        }
        return found.Values.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// A character's pain and death sounds (<c>data/hurt/&lt;id&gt;/0-3.ogg|wav</c>, as BioRand's
    /// <c>VoiceRandomiser</c> reads them: 0-2 hurt, 3 death), by number; the loose folder first.
    /// Their length is not read (0).
    /// </summary>
    public IReadOnlyDictionary<int, VoiceClip> HurtClips(string id)
    {
        var folder = id.Split('$')[0].ToLowerInvariant();
        var found = new Dictionary<int, VoiceClip>();
        void Add(string name, string source, string? entry)
        {
            var stem = Path.GetFileNameWithoutExtension(name);
            var ext = Path.GetExtension(name).ToLowerInvariant();
            if ((ext == ".ogg" || ext == ".wav") && int.TryParse(stem, out var n) && n is >= 0 and <= 3)
                found.TryAdd(n, new VoiceClip(folder, name, source, entry, 0));
        }
        var loose = Path.Combine(Directory, "data", "hurt", folder);
        if (System.IO.Directory.Exists(loose))
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(loose))
                Add(Path.GetFileName(file), file, null);
        }
        var packs = Path.Combine(Directory, "datapacks");
        if (System.IO.Directory.Exists(packs))
        {
            var prefix = $"data/hurt/{folder}/";
            foreach (var zip in System.IO.Directory.EnumerateFiles(packs, "*.zip").Order(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    using var archive = ZipFile.OpenRead(zip);
                    foreach (var e in archive.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && e.FullName.Length > prefix.Length))
                        Add(e.Name, zip, e.FullName);
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    // a broken or locked pack: the others still count
                }
            }
        }
        return found;
    }

    public static byte[] ReadOgg(VoiceClip clip)
    {
        if (clip.Entry == null)
            return File.ReadAllBytes(clip.Source);
        using var archive = ZipFile.OpenRead(clip.Source);
        var entry = archive.GetEntry(clip.Entry) ?? throw new FileNotFoundException($"{clip.Entry} is not in {clip.Source}.");
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>The character's portrait (<c>face.png</c> next to its PLD), or null when it has none.</summary>
    public static byte[]? ReadFace(LibraryCharacter character)
    {
        const string Face = "face.png";
        if (character.Entry == null)
        {
            var path = Path.Combine(Path.GetDirectoryName(character.Source)!, Face);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        using var archive = ZipFile.OpenRead(character.Source);
        var entry = archive.GetEntry(character.Entry[..(character.Entry.LastIndexOf('/') + 1)] + Face);
        if (entry == null)
            return null;
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public static byte[] ReadPld(LibraryCharacter character)
    {
        if (character.Entry == null)
            return File.ReadAllBytes(character.Source);
        using var archive = ZipFile.OpenRead(character.Source);
        var entry = archive.GetEntry(character.Entry) ?? throw new FileNotFoundException($"{character.Entry} is not in {character.Source}.");
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
