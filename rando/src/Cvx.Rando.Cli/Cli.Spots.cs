using Cvx.Formats;
using Cvx.Formats.Rdx;
using Cvx.Rando.Data;
using Cvx.Rando.Enemies;

namespace Cvx.Rando.Cli;

// enemy-spots: where random enemy placements may put enemies.
internal static partial class Cli
{
    private static int EnemySpotsCommand(Options options)
    {
        using var rooms = RoomArchive.Open(OpenData(options));
        var data = RandoData.Load();
        var ids = options.OptionalPositional(0) is { } name ? [RoomId.ParseName(name)] : RoomTable.All.ToArray();
        var png = options.Get("png");
        if (png != null)
            Directory.CreateDirectory(png);
        foreach (var id in ids)
        {
            var map = EnemySpots.Map(rooms.ReadRoom(id), EnemySpots.Known(id, rooms, data));
            Console.WriteLine($"{id.Name} ({id.BioRandId}): {map.Spots.Count} spots, {map.Reachable.Count} reachable cells, {map.Floor.Count} floor areas, {map.Known.Count} known");
            if (png == null)
                continue;
            // one picture per floor height
            var levels = map.Reachable.Select(p => p.Y).Distinct().Order().ToList();
            if (levels.Count == 0)
                levels.Add(0);
            foreach (var y in levels)
                File.WriteAllBytes(Path.Combine(png, levels.Count == 1 ? $"{id.Name}.png" : $"{id.Name}_y{y:0}.png"), DrawSpots(map, y));
        }
        return 0;
    }

    /// <summary>
    /// A room from above: floor grey, walls dark red (stairs and slopes orange), reachable cells
    /// light, spots yellow (pink: room for small enemies only) (walls at the picture's floor height dark red, at other heights faint purple); one picture per floor height, entrances green, known enemy places blue. X runs right, Z down.
    /// </summary>
    private static byte[] DrawSpots(EnemySpots.SpotMap map, float y)
    {
        // the reachable floor and what is known, with a margin
        var points = map.Reachable.Concat(map.Entrances).Concat(map.Known).ToList();
        if (points.Count == 0)
            points = map.Floor.Select(f => new RdxVectorF(f.X, f.Y, f.Z)).Append(new RdxVectorF()).ToList();
        float minX = points.Min(p => p.X) - 20, maxX = points.Max(p => p.X) + 20;
        float minZ = points.Min(p => p.Z) - 20, maxZ = points.Max(p => p.Z) + 20;
        const float scale = 4;
        var width = Math.Clamp((int)((maxX - minX) * scale) + 1, 1, 4000);
        var height = Math.Clamp((int)((maxZ - minZ) * scale) + 1, 1, 4000);
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < rgba.Length; i += 4)
            (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = ((byte)24, (byte)24, (byte)24, (byte)255);

        void Rect(float x, float z, float w, float d, (byte R, byte G, byte B, byte A) c)
        {
            int x0 = (int)((x - minX) * scale), x1 = (int)((x + w - minX) * scale);
            int z0 = (int)((z - minZ) * scale), z1 = (int)((z + d - minZ) * scale);
            for (var py = Math.Max(0, z0); py <= Math.Min(height - 1, z1); py++)
            {
                for (var px = Math.Max(0, x0); px <= Math.Min(width - 1, x1); px++)
                {
                    var o = (py * width + px) * 4;
                    var a = c.A / 255f;
                    rgba[o] = (byte)(rgba[o] * (1 - a) + c.R * a);
                    rgba[o + 1] = (byte)(rgba[o + 1] * (1 - a) + c.G * a);
                    rgba[o + 2] = (byte)(rgba[o + 2] * (1 - a) + c.B * a);
                }
            }
        }
        void Dot(RdxVectorF p, float r, (byte, byte, byte, byte) c) => Rect(p.X - r, p.Z - r, 2 * r, 2 * r, c);

        foreach (var f in map.Floor.Where(f => Math.Abs(f.Y - y) < 1.5f))
            Rect(f.X, f.Z, f.W, f.D, (90, 90, 90, 255));
        foreach (var p in map.Reachable.Where(p => p.Y == y))
            Dot(p, EnemySpots.Cell / 2, (140, 140, 140, 255));
        foreach (var w in map.Walls)
        {
            var here = y + 18 >= w.Bottom && y <= w.Top;
            Rect(w.X, w.Z, w.W, w.D, w.Type == 6 ? ((byte)230, (byte)140, (byte)30, (byte)110)
                : here ? ((byte)170, (byte)30, (byte)30, (byte)150) : ((byte)120, (byte)60, (byte)160, (byte)50));
        }
        foreach (var s in map.Spots.Where(s => s.Position.Y == y))
            Dot(s.Position, 0.6f, s.Room >= EnemySpots.Clearances[^1] ? ((byte)250, (byte)220, (byte)40, (byte)255) : ((byte)240, (byte)150, (byte)200, (byte)255));
        foreach (var k in map.Known.Where(k => Math.Abs(k.Y - y) < 1.5f))
            Dot(k, 1.5f, (60, 120, 255, 255));
        foreach (var e in map.Entrances.Where(e => Math.Abs(e.Y - y) < 1.5f))
            Dot(e, 2f, (40, 220, 80, 255));
        return Formats.Png.Encode(width, height, rgba);
    }
}
