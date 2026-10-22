using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Cvx.Formats;
using Cvx.Formats.Rdx;
using Cvx.Formats.Script;

namespace Cvx.Rando.Enemies;

/// <summary>
/// What every room of the game holds for enemy placements: where its enemies stand and the
/// death flags its scripts use. Read once per archive, the rooms in parallel (expanding all of
/// them takes a while).
/// </summary>
internal static class EnemyScan
{
    /// <param name="DeathFlags">Flags of <c>set_ck_enemy</c>, <c>ck_death</c> and <c>ck_death_event</c>.</param>
    /// <param name="StoryFlags">Death flags its scripts check or set directly (<c>ck</c>/<c>set</c> group 3).</param>
    public sealed record Room(IReadOnlyList<RdxVectorF> Positions, IReadOnlyList<int> DeathFlags, IReadOnlyList<int> StoryFlags);

    // (Lazy: callers on several threads at once must not each read every room)
    private static readonly ConditionalWeakTable<RoomArchive, Lazy<IReadOnlyDictionary<RoomId, Room>>> Cache = [];

    public static IReadOnlyDictionary<RoomId, Room> Of(RoomArchive archive) =>
        Cache.GetValue(archive, a => new Lazy<IReadOnlyDictionary<RoomId, Room>>(() => Read(a))).Value;

    private static IReadOnlyDictionary<RoomId, Room> Read(RoomArchive archive)
    {
        var result = new ConcurrentDictionary<RoomId, Room>();
        Parallel.ForEach(RoomTable.All, id =>
        {
            var file = archive.ReadRoom(id);
            var death = new List<int>();
            var story = new List<int>();
            foreach (var o in CvScriptReader.ReadOpcodes(file))
            {
                var b = o.Bytes.Span;
                if (b.Length < 4)
                    continue;
                var flag = (b[2] | b[3] << 8) & 0x3FF;
                if (o.Name is "set_ck_enemy" or "ck_death" or "ck_death_event")
                    death.Add(flag);
                else if (o.Name is "ck" or "set" && b.Length >= 6 && b[1] == 3)
                    story.Add(flag);
            }
            result[id] = new Room(file.Enemies.ToArray().Select(e => e.Position).ToList(), death, story);
        });
        return result;
    }
}
