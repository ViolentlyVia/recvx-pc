namespace Cvx.Rando.Data;

/// <summary>data/enemies.json: the enemy types the randomizer places and the rooms it copies them from.</summary>
public sealed class EnemyCatalog
{
    public List<EnemyKind> Types { get; set; } = [];
    /// <summary>Enemy types that may be replaced.</summary>
    public int[] Replaceable { get; set; } = [];
    /// <summary>Script opcodes that tie a room's enemies to events: such rooms keep them.</summary>
    public string[] ScriptedOpcodes { get; set; } = [];
    /// <summary>Script opcodes that take control of one enemy (byte 1 = 1, byte 2 = enemy index).</summary>
    public string[] WorkOpcodes { get; set; } = [];
    /// <summary>More rooms that keep their enemies (BioRand ids).</summary>
    public string[] KeepRooms { get; set; } = [];
}

public sealed class EnemyKind
{
    public string Name { get; set; } = "";
    public int Type { get; set; }
    /// <summary>Model version, when the type has several looks that act differently (Hunter, Sweeper).</summary>
    public int? Version { get; set; }
    /// <summary>0 (easy) to 3 (hard).</summary>
    public int Difficulty { get; set; }
    /// <summary>How often it comes up compared with other kinds of its difficulty.</summary>
    public double Weight { get; set; } = 1;
    /// <summary>The most in one room, per enemy difficulty setting.</summary>
    public int[] Max { get; set; } = [];
    /// <summary>"RM_xxxx#index": enemies to copy.</summary>
    public string[] Donors { get; set; } = [];
    /// <summary>The behaviour (<c>type</c> field) of placed enemies.</summary>
    public short Effect { get; set; }

    public int MaxAt(int difficulty) => Max.Length == 0 ? int.MaxValue : Max[Math.Clamp(difficulty, 0, Max.Length - 1)];

    public override string ToString() => Name;
}
