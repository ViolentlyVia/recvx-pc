using System.Reflection;
using System.Text.Json;
using Cvx.Formats;

namespace Cvx.Rando.Data;

/// <summary>The randomizer's built-in data (the files under <c>data/</c>).</summary>
public sealed class RandoData
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public RoomMap Map { get; }
    public IReadOnlyList<EnemyPosition> EnemyPositions { get; }
    public ItemCatalog Items { get; }
    public EnemyCatalog Enemies { get; }

    private RandoData(RoomMap map, IReadOnlyList<EnemyPosition> enemyPositions, ItemCatalog items, EnemyCatalog enemies)
    {
        Map = map;
        EnemyPositions = enemyPositions;
        Items = items;
        Enemies = enemies;
    }

    private static readonly Lazy<RandoData> BuiltIn = new(() => new RandoData(
        Load<RoomMap>("data/recv/rdt.json"),
        Load<List<EnemyPosition>>("data/recv/enemy.json"),
        new ItemCatalog(Load<ItemCatalogFile>("data/items.json").Items),
        Load<EnemyCatalog>("data/enemies.json")));

    public static RandoData Load() => BuiltIn.Value;

    public static string ReadResourceText(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing built-in data {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static T Load<T>(string name) =>
        JsonSerializer.Deserialize<T>(ReadResourceText(name), JsonOptions)
        ?? throw new InvalidDataException($"{name} is empty.");
}

/// <summary>A possible enemy position (data/recv/enemy.json): room, position, direction, floor.</summary>
public sealed class EnemyPosition
{
    public string Room { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public int D { get; set; }
    public int F { get; set; }

    public RoomId RoomId => RoomId.ParseBioRand(Room);
}
