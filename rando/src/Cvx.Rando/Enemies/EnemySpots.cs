using Cvx.Formats;
using Cvx.Formats.Rdx;
using Cvx.Rando.Data;

namespace Cvx.Rando.Enemies;

/// <summary>
/// Where new enemies can stand in a room, worked out from the room's own data (BioRand only has
/// recorded positions for a few Code: Veronica rooms):
/// <list type="bullet">
/// <item>walls: <c>walp</c> types 0-1 (boxes), 2-3 (circles) and 4-5 (right triangles) block,
///   as in hitchk.c <c>bhCheckWall</c>; type 6 (stairs and slopes) is walkable but no place to
///   start on;</item>
/// <item>the floor: a flood fill over a grid, at the height of each entrance and of the room's
///   enemies (in every version of the room), through the gaps the player fits through. A fill
///   that gets out of the walls (an open edge, or a height with no walls such as a bat's) is
///   not floor; there only the footstep areas count (<c>flrp</c> type 1, which set
///   <c>flr_snd</c> in hitchk.c and lie on the floor the player walks on). Only floor the
///   player gets to counts: a fill from an entrance, or one stairs or a slope join to it;</item>
/// <item>fair: away from the entrances (an enemy is not on top of the player as they come in)
///   and from the doors, and with room around it (1 unit is about 10 cm; a zombie's radius is
///   3.8 and its height 18.2, a spider's radius 6): each spot keeps how much room it has, and an
///   enemy only starts where it fits.</item>
/// </list>
/// </summary>
public static class EnemySpots
{
    /// <summary>Grid step.</summary>
    public const float Cell = 2f;
    /// <summary>The room to walls a spot is measured in (an enemy needs its radius and a little more).</summary>
    public static readonly float[] Clearances = [4.5f, 5.5f, 6.5f];
    /// <summary>Room to walls the flood fill needs to pass (about the player's width).</summary>
    public const float Passage = 2f;
    /// <summary>How far from an entrance an enemy may start; nearer when the room has no place that far.</summary>
    public static readonly float[] EntranceDistances = [30f, 20f];
    public const float DoorDistance = 10f;
    private const float EnemyHeight = 18f;
    /// <summary>Heights closer than this are one floor.</summary>
    private const float SameFloor = 1.5f;

    /// <summary>A place on the floor an enemy can start from, with <paramref name="Room"/> to the walls around it.</summary>
    public sealed record Spot(RdxVectorF Position, float Room)
    {
        /// <summary>Whether an enemy of this radius (<c>ar</c>) starts clear of the walls here.</summary>
        public bool Fits(float radius) => Room >= radius + 0.5f;
    }

    private readonly record struct Area(int Flags, int Type, int Id, int Floor, int Attr, float X, float Y, float Z, float W, float H, float D)
    {
        public static Area Of(int head, int attr, RdxVectorF p, RdxVectorF s) =>
            new(head & 0xFF, (head >> 8) & 0xFF, (head >> 16) & 0xFF, (sbyte)(head >> 24), attr, p.X, p.Y, p.Z, s.X, s.Y, s.Z);

        public bool On => (Flags & 1) != 0;
        public float MinX => Math.Min(X, X + W);
        public float MaxX => Math.Max(X, X + W);
        public float MinZ => Math.Min(Z, Z + D);
        public float MaxZ => Math.Max(Z, Z + D);
        public bool Contains(float x, float z, float margin = 0) =>
            x >= MinX - margin && x <= MaxX + margin && z >= MinZ - margin && z <= MaxZ + margin;
    }

    /// <summary>
    /// Positions known to be floor in a room: its enemies' in every version of the room, and
    /// BioRand's recorded ones.
    /// </summary>
    public static List<RdxVectorF> Known(RoomId id, RoomArchive archive, RandoData data)
    {
        var result = new List<RdxVectorF>();
        foreach (var (version, room) in EnemyScan.Of(archive).Where(r => r.Key.Stage == id.Stage && r.Key.Room == id.Room).OrderBy(r => r.Key))
            result.AddRange(room.Positions);
        result.AddRange(data.EnemyPositions.Where(p => p.RoomId.Stage == id.Stage && p.RoomId.Room == id.Room)
            .Select(p => new RdxVectorF(p.X, p.Y, p.Z)));
        return result;
    }

    /// <summary>
    /// The places in <paramref name="room"/>; <paramref name="known"/> are positions that may be
    /// floor (see <see cref="Known"/>).
    /// </summary>
    public static IReadOnlyList<Spot> Find(RdxFile room, IEnumerable<RdxVectorF> known) => Map(room, known).Spots;

    /// <summary>What <see cref="Find"/> works with, for drawing (the CLI's enemy-spots).</summary>
    public sealed record SpotMap(
        IReadOnlyList<(float X, float Z, float W, float D, float Y)> Floor,
        IReadOnlyList<(int Type, float X, float Z, float W, float D, float Bottom, float Top)> Walls,
        IReadOnlyList<RdxVectorF> Reachable,
        IReadOnlyList<Spot> Spots,
        IReadOnlyList<RdxVectorF> Entrances,
        IReadOnlyList<RdxVectorF> Known);

    public static SpotMap Map(RdxFile room, IEnumerable<RdxVectorF> known)
    {
        var knownList = known.ToList();
        var roomHeight = room.AfterCounts.Length >= 0x1C ? BitConverter.ToSingle(room.AfterCounts.Span[0x18..]) : 10000f;
        var walls = room.Boundaries.ToArray().Select(b => Area.Of(b.Type, b.Flags1, b.Position, b.Size))
            .Where(a => a.On && a.Type <= 6).ToList();
        var floor = room.Triggers.ToArray().Select(t => Area.Of(t.Type, t.Attrs1, t.Position, t.Size))
            .Where(a => a.On && a.Type == 1).ToList();
        var doors = room.Aots.ToArray().Where(a => a.Kind == RdxAot.KindDoor && (a.Unk00 & 1) != 0)
            .Select(a => Area.Of(0, 0, a.Position, a.Size)).ToList();
        var entrances = room.Players.ToArray().Select(p => p.Position).ToList();

        bool Blocked(float x, float y, float z, float r, bool slopes)
        {
            foreach (var w in walls)
            {
                // as bhCheckWall: no height is the room's; a negative one never meets anything
                var h = w.H == 0 ? roomHeight : w.H;
                if (y + EnemyHeight < w.Y || y > w.Y + h)
                    continue;
                switch (w.Type)
                {
                    case 0 or 1:
                        if (w.Contains(x, z, r))
                            return true;
                        break;
                    case 4 or 5:
                        if (w.Contains(x, z, r) && InTriangle(w, x, z, r))
                            return true;
                        break;
                    case 2 or 3:
                        if (MathF.Sqrt((x - w.X) * (x - w.X) + (z - w.Z) * (z - w.Z)) < w.W + r)
                            return true;
                        break;
                    case 6:
                        if (slopes && w.Contains(x, z, r))
                            return true;
                        break;
                }
            }
            return false;
        }

        // The grid covers the walls (but not whole-room ones) and everything else known, with a
        // border a fill that gets out of the walls reaches.
        var boxes = walls.Where(w => w.Type is 0 or 1 or 4 or 5).Select(w => (w.MinX, w.MaxX, w.MinZ, w.MaxZ))
            .Concat(walls.Where(w => w.Type is 2 or 3).Select(w => (w.X - w.W, w.X + w.W, w.Z - w.W, w.Z + w.W)))
            .Concat(floor.Select(a => (a.MinX, a.MaxX, a.MinZ, a.MaxZ)))
            .Concat(entrances.Concat(knownList).Select(p => (p.X, p.X, p.Z, p.Z)))
            .ToList();
        var reachable = new List<RdxVectorF>();
        if (boxes.Count != 0)
        {
            var minX = boxes.Min(b => b.Item1) - 2 * Cell;
            var maxX = boxes.Max(b => b.Item2) + 2 * Cell;
            var minZ = boxes.Min(b => b.Item3) - 2 * Cell;
            var maxZ = boxes.Max(b => b.Item4) + 2 * Cell;
            var columns = (int)((maxX - minX) / Cell) + 1;
            var rows = (int)((maxZ - minZ) / Cell) + 1;
            if ((long)columns * rows > 2_000_000)
                return new SpotMap(Floors(floor), Walls(walls), [], [], entrances, knownList);

            // One fill per floor height (entrances and known places that are near each other).
            var seeds = entrances.Concat(knownList).OrderBy(p => p.Y).ToList();
            var levels = new List<List<RdxVectorF>>();
            foreach (var s in seeds)
            {
                if (levels.Count != 0 && s.Y - levels[^1][0].Y < SameFloor)
                    levels[^1].Add(s);
                else
                    levels.Add([s]);
            }
            var regions = new List<(List<RdxVectorF> Points, bool Entrance)>();
            foreach (var level in levels)
            {
                var y = level[0].Y;
                var seen = new HashSet<(int, int)>();
                foreach (var s in level)
                {
                    var start = ((int)MathF.Round((s.X - minX) / Cell), (int)MathF.Round((s.Z - minZ) / Cell));
                    if (seen.Contains(start))
                        continue;
                    var region = Fill(start, columns, rows, (i, k) => !Blocked(minX + i * Cell, y, minZ + k * Cell, Passage, false), out var escaped);
                    // (a start next to a wall can begin inside a region already filled)
                    if (region == null || seen.Overlaps(region))
                        continue;
                    seen.UnionWith(region);
                    var points = region.Select(c => new RdxVectorF(minX + c.Item1 * Cell, y, minZ + c.Item2 * Cell));
                    // a fill out of the walls: only footstep areas at this height
                    if (escaped)
                        points = points.Where(p => floor.Any(a => Math.Abs(a.Y - y) < SameFloor && a.Contains(p.X, p.Z)));
                    var list = points.ToList();
                    var entrance = level.Any(e => entrances.Contains(e) && list.Any(p => Dist2(p, e) < 4 * Cell * Cell * 4));
                    regions.Add((list, entrance));
                }
            }

            // The player's floor: regions with an entrance, and those stairs or a slope lead to
            // from one (a region only an enemy stands in may be a pit or behind glass).
            var slopes = walls.Where(w => w.Type == 6).ToList();
            var accepted = regions.Where(r => r.Entrance && r.Points.Count != 0).Select(r => r.Points).ToList();
            var pending = regions.Where(r => !r.Entrance && r.Points.Count != 0).Select(r => r.Points).ToList();
            bool Touches(List<RdxVectorF> region, Area slope) => region.Any(p => slope.Contains(p.X, p.Z, 2 * Cell));
            for (var changed = true; changed;)
            {
                changed = false;
                foreach (var r in pending.ToList())
                {
                    if (slopes.Any(sl => Touches(r, sl) && accepted.Any(a => Touches(a, sl))))
                    {
                        accepted.Add(r);
                        pending.Remove(r);
                        changed = true;
                    }
                }
            }
            reachable.AddRange(accepted.SelectMany(r => r));
        }

        var open = reachable.Where(p => !doors.Any(d => d.Contains(p.X, p.Z, DoorDistance)))
            .Select(p => new Spot(p, Clearances.TakeWhile(r => !Blocked(p.X, p.Y, p.Z, r, true)).LastOrDefault()))
            .Where(s => s.Room != 0)
            .ToList();
        foreach (var distance in EntranceDistances)
        {
            var spots = open.Where(s => entrances.All(e => Dist2(e, s.Position) + (e.Y - s.Position.Y) * (e.Y - s.Position.Y) >= distance * distance)).ToList();
            if (spots.Count != 0)
                return new SpotMap(Floors(floor), Walls(walls), reachable, spots, entrances, knownList);
        }
        return new SpotMap(Floors(floor), Walls(walls), reachable, [], entrances, knownList);
    }

    /// <summary>
    /// The cells reached from <paramref name="start"/> through open ones (null when the start is
    /// not open); <paramref name="escaped"/> when the fill reached the grid's edge.
    /// </summary>
    private static HashSet<(int, int)>? Fill((int I, int K) start, int columns, int rows, Func<int, int, bool> open, out bool escaped)
    {
        escaped = false;
        // a start just inside a wall (an entrance by a door) begins at the nearest open cell
        (int, int)? first = null;
        for (var r = 0; r <= 3 && first == null; r++)
        {
            for (var di = -r; di <= r && first == null; di++)
            {
                for (var dk = -r; dk <= r && first == null; dk++)
                {
                    var (i, k) = (start.I + di, start.K + dk);
                    if (i >= 0 && k >= 0 && i < columns && k < rows && open(i, k))
                        first = (i, k);
                }
            }
        }
        if (first == null)
            return null;
        var region = new HashSet<(int, int)> { first.Value };
        var queue = new Queue<(int I, int K)>([first.Value]);
        while (queue.Count != 0)
        {
            var (i, k) = queue.Dequeue();
            if (i == 0 || k == 0 || i == columns - 1 || k == rows - 1)
                escaped = true;
            foreach (var next in new[] { (i + 1, k), (i - 1, k), (i, k + 1), (i, k - 1) })
            {
                if (next.Item1 < 0 || next.Item2 < 0 || next.Item1 >= columns || next.Item2 >= rows || region.Contains(next) || !open(next.Item1, next.Item2))
                    continue;
                region.Add(next);
                queue.Enqueue(next);
            }
        }
        return region;
    }

    /// <summary>
    /// Whether a point is in a triangle wall or nearer than <paramref name="r"/> to it: the right
    /// triangle (x, z), (x + w, z), (x, z + d) (hitchk.c <c>bhCheckInnerTriangle</c>; its id only
    /// says which way the triangle grows by a radius).
    /// </summary>
    private static bool InTriangle(Area w, float x, float z, float r)
    {
        (float X, float Z) a = (w.X, w.Z), b = (w.X + w.W, w.Z), c = (w.X, w.Z + w.D);
        static float Cross((float X, float Z) o, (float X, float Z) p, float x, float z) => (p.X - o.X) * (z - o.Z) - (p.Z - o.Z) * (x - o.X);
        var d1 = Cross(a, b, x, z);
        var d2 = Cross(b, c, x, z);
        var d3 = Cross(c, a, x, z);
        if (!((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0)))
            return true;
        static float Segment((float X, float Z) p, (float X, float Z) q, float x, float z)
        {
            var (dx, dz) = (q.X - p.X, q.Z - p.Z);
            var t = Math.Clamp(((x - p.X) * dx + (z - p.Z) * dz) / (dx * dx + dz * dz), 0, 1);
            var (ex, ez) = (p.X + t * dx - x, p.Z + t * dz - z);
            return MathF.Sqrt(ex * ex + ez * ez);
        }
        return Math.Min(Segment(a, b, x, z), Math.Min(Segment(b, c, x, z), Segment(c, a, x, z))) < r;
    }

    private static List<(float, float, float, float, float)> Floors(List<Area> floor) =>
        floor.Select(a => (a.MinX, a.MinZ, a.MaxX - a.MinX, a.MaxZ - a.MinZ, a.Y)).ToList();

    private static List<(int, float, float, float, float, float, float)> Walls(List<Area> walls) =>
        walls.Select(w => w.Type is 2 or 3
            ? (w.Type, w.X - w.W, w.Z - w.W, 2 * w.W, 2 * w.W, w.Y, w.H == 0 ? float.MaxValue : w.Y + w.H)
            : (w.Type, w.MinX, w.MinZ, w.MaxX - w.MinX, w.MaxZ - w.MinZ, w.Y, w.H == 0 ? float.MaxValue : w.Y + w.H)).ToList();

    private static float Dist2(RdxVectorF a, RdxVectorF b) => (a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z);
}
