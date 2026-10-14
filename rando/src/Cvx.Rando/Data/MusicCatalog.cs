namespace Cvx.Rando.Data;

/// <summary>data/music.json: the music tracks the shuffle may swap, by category.</summary>
public sealed class MusicCatalog
{
    public List<MusicCategory> Categories { get; set; } = [];
}

public sealed class MusicCategory
{
    public string Name { get; set; } = "";
    public int[] Tracks { get; set; } = [];
}
