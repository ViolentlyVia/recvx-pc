namespace Cvx.Rando.Data;

/// <summary>data/npcs.json: the NPCs that may take each other's look and voice, and who speaks each voice line.</summary>
public sealed class NpcCatalog
{
    public List<NpcCharacter> Characters { get; set; } = [];
    /// <summary>
    /// Claire and Chris: never shuffled with the NPCs, but each may take a BioRand character's
    /// look (their cutscene models, types 91 and 92) and voice (<see cref="RandoSettings.RandomPlayers"/>).
    /// </summary>
    public List<NpcCharacter> Players { get; set; } = [];
    /// <summary>Disc ("1", "2") -> VOICE?.AFS entry -> the speaker's <see cref="NpcCharacter.Voice"/>.</summary>
    public Dictionary<string, Dictionary<int, string>> Voices { get; set; } = [];
}

public sealed class NpcCharacter
{
    public string Name { get; set; } = "";
    /// <summary>The name of the speaker in <see cref="NpcCatalog.Voices"/>.</summary>
    public string Voice { get; set; } = "";
    /// <summary>Their enemy types in the rooms.</summary>
    public int[] Types { get; set; } = [];
    /// <summary>"RM_xxxx:index": the enemy entry whose model and textures give their look.</summary>
    public string Look { get; set; } = "";
}
