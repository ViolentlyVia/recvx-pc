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
    /// <summary>A kind's ratio when the settings do not give one (BioRand's sliders start there; 0-7).</summary>
    public const int DefaultRatio = 4;
    public const int MaxRatio = 7;

    public string Name { get; set; } = "";
    /// <summary>Names it had before: settings that use them still count for it.</summary>
    public string[] Aliases { get; set; } = [];
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
    /// <summary>Its collision radius (<c>ar</c>): random placements start it where it has this much room.</summary>
    public float Radius { get; set; } = 4;
    /// <summary>Its colour in the window's enemy bar ("#rrggbb").</summary>
    public string Colour { get; set; } = "#808080";
    /// <summary>Not yet seen working in the game: never placed unless the settings give it a ratio.</summary>
    public bool Untested { get; set; }

    /// <summary>Its ratio in these settings' mix (0-7).</summary>
    public int RatioIn(RandoSettings settings)
    {
        foreach (var name in Aliases.Prepend(Name))
        {
            if (settings.EnemyRatios.TryGetValue(name, out var r))
                return Math.Clamp(r, 0, MaxRatio);
        }
        return Untested ? 0 : DefaultRatio;
    }

    public int MaxAt(int difficulty) => Max.Length == 0 ? int.MaxValue : Max[Math.Clamp(difficulty, 0, Max.Length - 1)];

    public override string ToString() => Name;
}
