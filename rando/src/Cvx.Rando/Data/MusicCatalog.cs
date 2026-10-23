namespace Cvx.Rando.Data;

/// <summary>data/music.json: the music tracks the shuffle may swap, by category.</summary>
public sealed class MusicCatalog
{
    /// <summary>Other games' music is matched to this loudness (RMS of 16-bit samples).</summary>
    public double TargetRms { get; set; }
    public List<MusicCategory> Categories { get; set; } = [];
}

public sealed class MusicCategory
{
    public string Name { get; set; } = "";
    /// <summary>BioRand's tags (data/bgm/&lt;game&gt;/&lt;tag&gt;) whose tracks may play in this category's place.</summary>
    public string[] BioRand { get; set; } = [];
    public int[] Tracks { get; set; } = [];
}
