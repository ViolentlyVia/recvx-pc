namespace Cvx.Formats;

/// <summary>
/// The extracted game data folder that recvx-pc uses (<c>data</c> next to recvx.exe, with
/// <c>files.idx</c>).
/// </summary>
public sealed class GameData
{
    public const string RoomArchiveName = "RDX_LNK.AFS";

    public string Directory { get; }
    public string RoomArchivePath => Path.Combine(Directory, RoomArchiveName);

    public GameData(string directory)
    {
        Directory = Path.GetFullPath(directory);
        if (!File.Exists(RoomArchivePath))
            throw new FileNotFoundException($"{RoomArchiveName} not found in {Directory}.", RoomArchivePath);
    }

    /// <summary>
    /// The default data folder: <c>CVX_DATA</c>, else the port's <c>build_pc\data</c> at the root of
    /// the repository (the randomizer is in its rando\ folder; for development), else null.
    /// </summary>
    public static string? FindDefaultDirectory()
    {
        var env = Environment.GetEnvironmentVariable("CVX_DATA");
        if (!string.IsNullOrEmpty(env) && File.Exists(Path.Combine(env, RoomArchiveName)))
            return env;

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "build_pc", "data");
            if (File.Exists(Path.Combine(candidate, RoomArchiveName)))
                return candidate;
        }
        return null;
    }

    public AfsArchive OpenRoomArchive()
    {
        var afs = AfsArchive.Open(RoomArchivePath);
        if (afs.Count != RoomTable.Count)
        {
            afs.Dispose();
            throw new InvalidDataException($"{RoomArchiveName} has {afs.Count} entries, expected {RoomTable.Count}.");
        }
        return afs;
    }

    /// <summary>Reads <c>files.idx</c>: one <c>start_sector size path</c> line per file.</summary>
    public IReadOnlyList<FilesIdxEntry> ReadFilesIdx()
    {
        var path = Path.Combine(Directory, "files.idx");
        var result = new List<FilesIdxEntry>();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
                throw new InvalidDataException($"Bad files.idx line: {raw}");
            result.Add(new FilesIdxEntry(long.Parse(parts[0]), long.Parse(parts[1]), parts[2]));
        }
        return result;
    }
}

public readonly record struct FilesIdxEntry(long StartSector, long Size, string Path);
