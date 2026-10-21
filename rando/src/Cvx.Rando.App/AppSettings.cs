using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cvx.Rando.App;

/// <summary>What the app remembers between runs: cvx-rando.json next to the program (or in AppData when that folder is read-only).</summary>
public sealed class AppSettings
{
    public string? GameDirectory { get; set; }
    /// <summary>BioRand's folder, for other games' music (found next to the program when not set).</summary>
    public string? BioRandDirectory { get; set; }
    public RandoSettings Rando { get; set; } = new() { Seed = Random.Shared.Next(1, 1_000_000) };
    /// <summary>The share code of the last seed generated: the window tells whether the options still match it.</summary>
    public string? LastGeneratedCode { get; set; }
    /// <summary>Where the window was (null at first run: centred).</summary>
    public WindowPlacement? Window { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static IEnumerable<string> Paths()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "cvx-rando.json");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "cvx-rando", "cvx-rando.json");
    }

    public static AppSettings Load()
    {
        foreach (var path in Paths())
        {
            try
            {
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new AppSettings();
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // a broken file starts over with the defaults
            }
        }
        return new AppSettings();
    }

    public void Save()
    {
        foreach (var path in Paths())
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // try the next place
            }
        }
    }
}

public sealed record WindowPlacement(int X, int Y, double Width, double Height, bool Maximized);
