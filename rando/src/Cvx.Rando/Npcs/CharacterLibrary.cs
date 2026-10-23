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
