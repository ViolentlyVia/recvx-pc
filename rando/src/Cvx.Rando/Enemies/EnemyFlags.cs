using System.Runtime.CompilerServices;
using Cvx.Formats;
using Cvx.Formats.Script;

namespace Cvx.Rando.Enemies;

/// <summary>
/// Enemy death flags (<c>ed_flg</c>, flag group 3, 1024 bits kept in the save). A room's script
/// names each enemy that stays dead: <c>set_ck_enemy [22][enemy][flag u16]</c> at the room's
/// start hides it when its flag is set (event.c <c>bhEneSetCk</c>, which reads only 9 bits), and
/// <c>ck_death [0D][enemy][flag u16]</c> every frame sets the flag when it dies
/// (<c>bhDieCk</c>). Some rooms also read death flags as story progress (<c>ck</c> with group 3),
/// and the game reads flag 349 itself (system.c).
/// </summary>
public sealed class EnemyFlags
{
    /// <summary>Flags <c>set_ck_enemy</c> can name.</summary>
    public const int Count = 512;

    public const byte SetCkEnemy = 0x22;
    public const byte CkDeath = 0x0D;
    private const byte Set = 0x05;
    private const byte Group = 3;

    private static readonly ConditionalWeakTable<RoomArchive, Lazy<EnemyFlags>> Cache = [];

    /// <summary>Every death flag the game's rooms use.</summary>
    public required IReadOnlySet<int> Used { get; init; }
    /// <summary>Death flags something reads besides the enemy's own room start (story checks).</summary>
    public required IReadOnlySet<int> Story { get; init; }

    public static EnemyFlags Of(RoomArchive archive) => Cache.GetValue(archive, a => new Lazy<EnemyFlags>(() => Read(a))).Value;

    private static EnemyFlags Read(RoomArchive archive)
    {
        var used = new HashSet<int> { 349 };
        var story = new HashSet<int> { 349 };
        foreach (var room in EnemyScan.Of(archive).Values)
        {
            used.UnionWith(room.DeathFlags);
            used.UnionWith(room.StoryFlags);
            story.UnionWith(room.StoryFlags);
        }
        return new EnemyFlags { Used = used, Story = story };
    }

    /// <summary>The death flag of each enemy of a room (by index) that has exactly one.</summary>
    public static Dictionary<int, int> ByEnemy(IEnumerable<CvOpcode> opcodes) =>
        opcodes.Where(o => o.Opcode == SetCkEnemy && o.Length >= 4)
            .Select(o => (Enemy: (int)o.Bytes.Span[1], Flag: (o.Bytes.Span[2] | o.Bytes.Span[3] << 8) & 0x1FF))
            .GroupBy(x => x.Enemy)
            .Where(g => g.Select(x => x.Flag).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().Flag);

    /// <summary><c>set_ck_enemy</c>: hides an enemy at the room's start once its flag is set.</summary>
    public static byte[] HideIfDead(int enemy, int flag) => [SetCkEnemy, (byte)enemy, (byte)flag, (byte)(flag >> 8)];

    /// <summary><c>ck_death</c>: sets the flag when the enemy dies.</summary>
    public static byte[] MarkDeath(int enemy, int flag) => [CkDeath, (byte)enemy, (byte)flag, (byte)(flag >> 8)];

    /// <summary><c>set</c> group 3: sets a death flag (the enemy counts as killed).</summary>
    public static byte[] MarkDead(int flag) => [Set, Group, (byte)flag, (byte)(flag >> 8), 0, 0];
}
