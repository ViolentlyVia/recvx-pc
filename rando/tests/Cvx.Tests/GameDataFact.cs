using Cvx.Formats;

namespace Cvx.Tests;

/// <summary>A test that needs the extracted game data; skipped when it cannot be found.</summary>
public sealed class GameDataFactAttribute : FactAttribute
{
    public GameDataFactAttribute()
    {
        if (TestData.Directory == null)
            Skip = "Game data not found (set CVX_DATA or build the port's build_pc\\data).";
    }
}

internal static class TestData
{
    public static string? Directory { get; } = GameData.FindDefaultDirectory();

    public static GameData Open() => new(Directory ?? throw new InvalidOperationException("No game data."));
}
