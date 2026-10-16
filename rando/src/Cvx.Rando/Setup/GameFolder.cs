using System.Text;
using Cvx.Formats;

namespace Cvx.Rando.Setup;

/// <summary>
/// recvx.ini: the lines are kept as they are, the last value of a key wins (like the game), and
/// setting a key replaces its last line and removes the others (like recvx-config's ini_set).
/// </summary>
public sealed class IniFile
{
    private readonly List<string> _lines;
    private readonly string _newline;

    public string Path { get; }

    private IniFile(string path, List<string> lines, string newline)
    {
        Path = path;
        _lines = lines;
        _newline = newline;
    }

    public static IniFile Load(string path)
    {
        if (!File.Exists(path))
            return new IniFile(path, [], "\r\n");
        var text = File.ReadAllText(path);
        var newline = text.Contains("\r\n") || !text.Contains('\n') ? "\r\n" : "\n";
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (lines.Count != 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return new IniFile(path, lines, newline);
    }

    private static bool Is(string line, string key) =>
        line.Length > key.Length && line[key.Length] == '=' && line.StartsWith(key, StringComparison.OrdinalIgnoreCase);

    public string? Get(string key)
    {
        for (var i = _lines.Count - 1; i >= 0; i--)
        {
            if (Is(_lines[i], key))
                return _lines[i][(key.Length + 1)..];
        }
        return null;
    }

    /// <summary>Null or empty removes the key.</summary>
    public void Set(string key, string? value)
    {
        var last = _lines.FindLastIndex(l => Is(l, key));
        var keep = string.IsNullOrEmpty(value) ? -1 : last;
        for (var i = _lines.Count - 1; i >= 0; i--)
        {
            if (i != keep && Is(_lines[i], key))
            {
                _lines.RemoveAt(i);
                if (keep > i)
                    keep--;
            }
        }
        if (string.IsNullOrEmpty(value))
            return;
        if (keep >= 0)
            _lines[keep] = $"{key}={value}";
        else
            _lines.Add($"{key}={value}");
    }

    public void Save()
    {
        var sb = new StringBuilder();
        foreach (var line in _lines)
            sb.Append(line).Append(_newline);
        File.WriteAllText(Path, sb.ToString(), new UTF8Encoding(false));
    }
}

/// <summary>
/// The game's folder (recvx.exe and recvx.ini) and what the randomizer does to it. A seed is
/// written to <c>rando\&lt;seed&gt;</c>; playing it sets <c>mod=</c> to that folder and
/// <c>saves=</c> to <c>saves_rando</c> (one save folder for random runs, emptied when a new seed
/// is generated), keeping the player's own <c>saves=</c> in
/// <c>saves_normal=</c> ("*" when there was none). Playing the normal game puts that back. The
/// same rules as recvx-config's "Play normal game".
/// </summary>
public sealed class GameFolder
{
    public const string ExeName = "recvx.exe";
    public const string IniName = "recvx.ini";
    public const string ConfigName = "recvx-config.exe";
    public const string RandoFolder = "rando";
    public const string SavesFolder = "saves_rando";

    public string Directory { get; }
    public string ExePath => System.IO.Path.Combine(Directory, ExeName);
    public string IniPath => System.IO.Path.Combine(Directory, IniName);
    public bool HasExe => File.Exists(ExePath);

    public GameFolder(string directory) => Directory = System.IO.Path.GetFullPath(directory);

    public IniFile ReadIni() => IniFile.Load(IniPath);

    /// <summary>A path from recvx.ini: absolute, or relative to the game folder (as the game reads it).</summary>
    public string Resolve(string path) =>
        System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.GetFullPath(System.IO.Path.Combine(Directory, path));

    /// <summary>The data folder the game uses: data= in recvx.ini, else the game's "data" folder.</summary>
    public string DataDirectory => ReadIni().Get("data") is { Length: > 0 } d ? Resolve(d) : System.IO.Path.Combine(Directory, "data");

    /// <summary>Whether the data folder is set up (files.idx and the room archive).</summary>
    public bool HasData =>
        File.Exists(System.IO.Path.Combine(DataDirectory, "files.idx")) &&
        File.Exists(System.IO.Path.Combine(DataDirectory, GameData.RoomArchiveName));

    public string SeedDirectory(int seed) => System.IO.Path.Combine(Directory, RandoFolder, seed.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>The mod folder recvx.ini points at, if any.</summary>
    public string? ActiveMod => ReadIni().Get("mod") is { Length: > 0 } m ? Resolve(m) : null;

    /// <summary>The seed of the active mod folder (from its rando.ini), if any.</summary>
    public string? ActiveSeed
    {
        get
        {
            var mod = ActiveMod;
            if (mod == null)
                return null;
            var rando = System.IO.Path.Combine(mod, "rando.ini");
            return File.Exists(rando) ? IniFile.Load(rando).Get("seed") ?? "?" : "?";
        }
    }

    /// <summary>The save folder of random runs.</summary>
    public string SavesDirectory => System.IO.Path.Combine(Directory, SavesFolder);

    /// <summary>Points the game at a seed's folder, with the random runs' save folder.</summary>
    public void Play(int seed)
    {
        var ini = ReadIni();
        if (ini.Get("saves_normal") == null)
            ini.Set("saves_normal", ini.Get("saves") is { Length: > 0 } own ? own : "*");
        var id = seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ini.Set("mod", $@"{RandoFolder}\{id}");
        ini.Set("saves", SavesFolder);
        ini.Save();
        System.IO.Directory.CreateDirectory(SavesDirectory);
    }

    /// <summary>Whether the random runs' save folder holds anything.</summary>
    public bool HasRandomSaves =>
        System.IO.Directory.Exists(SavesDirectory) && System.IO.Directory.EnumerateFileSystemEntries(SavesDirectory).Any();

    /// <summary>
    /// Empties the random runs' save folder, so a new seed starts with no saves (this also
    /// removes the per-seed folders older versions made in it). Never touches the player's own saves.
    /// </summary>
    public void ClearRandomSaves()
    {
        var dir = new DirectoryInfo(SavesDirectory);
        if (!dir.Exists)
            return;
        foreach (var file in dir.EnumerateFiles())
            file.Delete();
        foreach (var sub in dir.EnumerateDirectories())
            sub.Delete(recursive: true);
    }

    /// <summary>Back to the normal game: no mod, the player's own saves.</summary>
    public void PlayNormal()
    {
        var ini = ReadIni();
        var normal = ini.Get("saves_normal");
        ini.Set("mod", null);
        if (normal != null)
            ini.Set("saves", normal == "*" ? null : normal);
        ini.Set("saves_normal", null);
        ini.Save();
    }

    /// <summary>
    /// One seed at a time: removes every seed folder in rando\ except <paramref name="keep"/>'s.
    /// Returns the folders that could not be removed (in use).
    /// </summary>
    public IReadOnlyList<string> DeleteOtherSeeds(int keep)
    {
        var failed = new List<string>();
        var kept = System.IO.Path.GetFullPath(SeedDirectory(keep)).TrimEnd('\\');
        foreach (var d in SeedFolders().Where(d => !string.Equals(d.FullName.TrimEnd('\\'), kept, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                d.Delete(recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(d.FullName);
            }
        }
        return failed;
    }

    /// <summary>The seed folders in rando\, newest first.</summary>
    public IReadOnlyList<DirectoryInfo> SeedFolders()
    {
        var dir = new DirectoryInfo(System.IO.Path.Combine(Directory, RandoFolder));
        return dir.Exists ? dir.GetDirectories().OrderByDescending(d => d.LastWriteTimeUtc).ToList() : [];
    }

    /// <summary>Finds the game folder: the given one, else this program's folder or its parents' recvx-pc build.</summary>
    public static GameFolder? Find(string? remembered, string programDirectory)
    {
        foreach (var candidate in new[] { remembered, programDirectory })
        {
            if (!string.IsNullOrEmpty(candidate) && File.Exists(System.IO.Path.Combine(candidate, ExeName)))
                return new GameFolder(candidate);
        }
        return null;
    }
}
