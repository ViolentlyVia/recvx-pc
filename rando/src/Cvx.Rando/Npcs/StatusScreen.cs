using Cvx.Formats;
using Cvx.Formats.Models;

namespace Cvx.Rando.Npcs;

/// <summary>A player's look on the status screen: the name to show and BioRand's portrait (<c>face.png</c>).</summary>
public sealed record StatusLook(string Name, byte[] Face);

/// <summary>
/// The status screen's player pictures. The screen's textures are ITEM1.AFS and ITEM2.AFS entry
/// 145 (fileview.c, <c>RequestReadInsideFile(sys-&gt;itm_partid, 145, ...)</c>), five texture
/// lists of which the third is a 512x512 8-bit page with each player's pieces: sub1.c
/// <c>StatusInit</c> gives <c>parts_10b</c>'s first five the pictures <c>ply_id</c>, +4, +8, +12
/// and +16 of <c>parts_14</c> (the UVs are in halves of a pixel): the face (88x88), the name plate
/// (88x20), the data card (name, height, weight, blood type; 140x60), a close-up of an eye
/// (84x48, red) and an emblem (80x72). Claire is player 0 and Chris 1. A player with a BioRand
/// look gets that character's face (scaled up: BioRand's are 40x56 with the name across the
/// top), the name on the plate and the card, and the card's other lines blanked (BioRand does
/// not know them). The eyes and the emblem stay: a close-up of one eye made from BioRand's
/// face would be a few of its pixels, a red blur. The page keeps its palette, the
/// colours of four faces; the new pictures take its nearest colours, and the few the rest of
/// the page no longer uses are given to the colours they miss most.
/// </summary>
public static class StatusScreen
{
    public static readonly string[] Archives = ["ITEM1.AFS", "ITEM2.AFS"];
    public const int Entry = 145;
    /// <summary>The page with the players' pictures: the entry's third TIM2 picture (texture 2 of the list).</summary>
    public const int Page = 2;

    private const int Size = 512;

    private static Rect Face(int player) => new(player * 88, 0, 88, 88);
    private static Rect Plate(int player) => new(352, player * 20, 88, 20);
    private static Rect Card(int player) => new(player % 2 * 140, 88 + player / 2 * 60, 140, 60);

    /// <summary>The name a look shows: its id's name, upper case ("kendo.re2" -> KENDO).</summary>
    public static string NameOf(string look) => look.Split('.')[0].ToUpperInvariant();

    /// <summary>The entry with <paramref name="looks"/> (player 0 Claire, 1 Chris -> look) drawn on its page.</summary>
    public static byte[] Make(ReadOnlySpan<byte> entry, IReadOnlyDictionary<int, StatusLook> looks)
    {
        var textures = TextureEntry.Read(entry);
        var records = textures.Records.Select((r, i) => (r, i)).Where(x => x.r.Code == TextureRecord.Tim2Code).ToList();
        var record = records[Page].i;
        var page = new Tim2Picture(textures.Records[record].Data);
        if (page.ImageType != 5 || page.Width != Size || page.Height != Size)
            throw new InvalidDataException($"ITEM AFS entry {Entry}: the players' page is {page.Width}x{page.Height} type {page.ImageType}, not {Size}x{Size} 8-bit.");

        var palette = page.Palette();
        var pixels = page.Image.ToArray();
        var canvas = new uint[pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
            canvas[i] = palette[pixels[i]];
        var changed = new bool[pixels.Length];

        foreach (var (player, look) in looks)
        {
            var face = FacePicture(look.Face);
            DrawFace(canvas, changed, Face(player), face);
            DrawPlate(canvas, changed, Plate(player), look.Name);
            DrawCard(canvas, changed, Card(player), look.Name);
        }

        var made = Tim2Picture.Create(page, page.GlobalIndex, Size, Size, Index(canvas, changed, pixels, palette), palette);
        textures.Records[record] = textures.Records[record] with { Data = made.Data };
        return textures.ToBytes();
    }

    // ---------------------------------------------------------------- pictures

    private readonly record struct Rect(int X, int Y, int W, int H)
    {
        public bool Contains(int x, int y) => x >= X && x < X + W && y >= Y && y < Y + H;
    }

    /// <summary>A picture as RGB floats (0-255), row by row.</summary>
    private sealed class Picture(int width, int height)
    {
        public int Width { get; } = width;
        public int Height { get; } = height;
        public float[] R { get; } = new float[width * height];
        public float[] G { get; } = new float[width * height];
        public float[] B { get; } = new float[width * height];

        /// <summary>Catmull-Rom at (x, y) in pixel units (pixel centres at +0.5).</summary>
        public (float R, float G, float B) Sample(float x, float y)
        {
            x -= 0.5f;
            y -= 0.5f;
            int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
            float fx = x - x0, fy = y - y0;
            Span<float> wx = stackalloc float[4], wy = stackalloc float[4];
            Weights(fx, wx);
            Weights(fy, wy);
            float r = 0, g = 0, b = 0;
            for (var j = 0; j < 4; j++)
            {
                var yy = Math.Clamp(y0 - 1 + j, 0, Height - 1);
                for (var i = 0; i < 4; i++)
                {
                    var xx = Math.Clamp(x0 - 1 + i, 0, Width - 1);
                    var w = wx[i] * wy[j];
                    var p = yy * Width + xx;
                    r += R[p] * w;
                    g += G[p] * w;
                    b += B[p] * w;
                }
            }
            return (Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
        }

        private static void Weights(float t, Span<float> w)
        {
            float t2 = t * t, t3 = t2 * t;
            w[0] = (-t3 + 2 * t2 - t) / 2;
            w[1] = (3 * t3 - 5 * t2 + 2) / 2;
            w[2] = (-3 * t3 + 4 * t2 + t) / 2;
            w[3] = (t3 - t2) / 2;
        }
    }

    /// <summary>
    /// BioRand's portrait without its name: RE2's faces (40x56) have it in the top 10 rows, on
    /// black; a picture of another size is taken whole.
    /// </summary>
    private static Picture FacePicture(byte[] png)
    {
        var (w, h, rgba) = Png.Decode(png);
        var top = w == 40 && h == 56 ? 10 : 0;
        var face = new Picture(w, h - top);
        for (var y = 0; y < face.Height; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var s = ((y + top) * w + x) * 4;
                var p = y * w + x;
                // see-through pixels on black, as RE2 shows them
                var a = rgba[s + 3] / 255f;
                face.R[p] = rgba[s] * a;
                face.G[p] = rgba[s + 1] * a;
                face.B[p] = rgba[s + 2] * a;
            }
        }
        return face;
    }

    /// <summary>The face scaled to the frame's width, a third of what does not fit cut from the top.</summary>
    private static void DrawFace(uint[] canvas, bool[] changed, Rect frame, Picture face)
    {
        var scale = (float)frame.W / face.Width;
        var skip = Math.Max(0, face.Height * scale - frame.H) / 3;
        for (var y = 0; y < frame.H; y++)
        {
            for (var x = 0; x < frame.W; x++)
            {
                var (r, g, b) = face.Sample((x + 0.5f) / scale, (y + 0.5f + skip) / scale);
                Set(canvas, changed, frame.X + x, frame.Y + y, Rgb(r, g, b));
            }
        }
    }

    /// <summary>The name in the plate's letters' colour on its ground, as large as the game's (8x11, bold) or narrower to fit.</summary>
    private static void DrawPlate(uint[] canvas, bool[] changed, Rect plate, string name)
    {
        var ground = Commonest(canvas, plate);
        var inside = Bounds(canvas, plate, c => c == ground);
        var ink = Colors(canvas, inside).Where(c => c != ground).MaxBy(Light);
        Fill(canvas, changed, inside, ground);
        const int height = 11, gap = 2;
        // 7 wide and 1 bolder, or narrower to fit
        var width = Math.Min(7f, (inside.W - 4 - gap * (name.Length - 1f)) / Math.Max(1, name.Length) * 8 / 9);
        var total = name.Length * width * 9 / 8 + (name.Length - 1) * gap;
        DrawText(canvas, changed, name, inside.X + (inside.W - total) / 2, inside.Y + (inside.H - height) / 2f, width, height, gap, width / 8, ink, inside);
    }

    /// <summary>The name on the card's first line, in its letters' colour and size (5x7 dots, bold); the other lines blanked.</summary>
    private static void DrawCard(uint[] canvas, bool[] changed, Rect card, string name)
    {
        var ground = Commonest(canvas, card);
        var inside = Bounds(canvas, card, c => c == ground);
        var ink = Colors(canvas, inside).MaxBy(Light);
        // the first line of writing: the rows from the first with the letters' colour to the next without
        var rows = Enumerable.Range(inside.Y, inside.H).Where(y => Enumerable.Range(inside.X, inside.W).Any(x => canvas[y * Size + x] == ink)).ToList();
        var left = rows.Count == 0 ? inside.X : Enumerable.Range(inside.X, inside.W).First(x => rows.Any(y => canvas[y * Size + x] == ink));
        Fill(canvas, changed, inside, ground);
        DrawText(canvas, changed, name, left, rows.Count == 0 ? inside.Y : rows[0], 5, 7, 2, 1, ink, inside);
    }

    /// <summary>
    /// Text in the 5x7 font, each letter stretched to width x height, its strokes <paramref name="bold"/>
    /// wider to the right, and smoothed (4x4 samples a pixel), the ink laid over what is there.
    /// </summary>
    private static void DrawText(uint[] canvas, bool[] changed, string text, float left, float top, float width, float height, float gap, float bold, uint ink, Rect clip)
    {
        var x0 = left;
        foreach (var ch in text)
        {
            if (Glyphs.TryGetValue(ch, out var rows))
            {
                float cw = width / 5, rh = height / 7;
                for (var y = (int)MathF.Floor(top); y < MathF.Ceiling(top + height); y++)
                {
                    for (var x = (int)MathF.Floor(x0); x < MathF.Ceiling(x0 + width + bold); x++)
                    {
                        if (!clip.Contains(x, y))
                            continue;
                        var hits = 0;
                        for (var sy = 0; sy < 4; sy++)
                        {
                            for (var sx = 0; sx < 4; sx++)
                            {
                                var px = x + (sx + 0.5f) / 4 - x0;
                                var row = (int)MathF.Floor((y + (sy + 0.5f) / 4 - top) / rh);
                                if (row is >= 0 and < 7 && (Dot(rows[row], px / cw) || Dot(rows[row], (px - bold) / cw)))
                                    hits++;
                            }
                        }
                        if (hits != 0)
                            Set(canvas, changed, x, y, Mix(canvas[y * Size + x], ink, hits / 16f));
                    }
                }
            }
            x0 += width + bold + gap;
        }

        static bool Dot(string row, float col) => col >= 0 && col < 5 && row[(int)col] == '#';
    }

    // ---------------------------------------------------------------- colours

    private static void Set(uint[] canvas, bool[] changed, int x, int y, uint color)
    {
        canvas[y * Size + x] = color;
        changed[y * Size + x] = true;
    }

    private static void Fill(uint[] canvas, bool[] changed, Rect r, uint color)
    {
        for (var y = r.Y; y < r.Y + r.H; y++)
        {
            for (var x = r.X; x < r.X + r.W; x++)
                Set(canvas, changed, x, y, color);
        }
    }

    private static IEnumerable<uint> Colors(uint[] canvas, Rect r)
    {
        for (var y = r.Y; y < r.Y + r.H; y++)
        {
            for (var x = r.X; x < r.X + r.W; x++)
                yield return canvas[y * Size + x];
        }
    }

    private static uint Commonest(uint[] canvas, Rect r) => Colors(canvas, r).GroupBy(c => c).MaxBy(g => g.Count())!.Key;

    /// <summary>The smallest rectangle around the pixels of <paramref name="r"/> that pass <paramref name="test"/>.</summary>
    private static Rect Bounds(uint[] canvas, Rect r, Func<uint, bool> test)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (var y = r.Y; y < r.Y + r.H; y++)
        {
            for (var x = r.X; x < r.X + r.W; x++)
            {
                if (!test(canvas[y * Size + x]))
                    continue;
                (x0, y0, x1, y1) = (Math.Min(x0, x), Math.Min(y0, y), Math.Max(x1, x), Math.Max(y1, y));
            }
        }
        return x1 < 0 ? r : new Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    /// <summary>An opaque colour (the PS2's alpha, 0x80).</summary>
    private static uint Rgb(float r, float g, float b) =>
        (uint)MathF.Round(r) | (uint)MathF.Round(g) << 8 | (uint)MathF.Round(b) << 16 | 0x80000000;

    private static float Light(uint c) => 0.299f * (c & 0xFF) + 0.587f * ((c >> 8) & 0xFF) + 0.114f * ((c >> 16) & 0xFF);

    private static uint Mix(uint a, uint b, float t)
    {
        float Ch(int shift) => ((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t;
        return (uint)MathF.Round(Ch(0)) | (uint)MathF.Round(Ch(8)) << 8 | (uint)MathF.Round(Ch(16)) << 16 | (a & 0xFF000000);
    }

    private static int Distance(uint a, uint b)
    {
        int dr = (int)(a & 0xFF) - (int)(b & 0xFF), dg = (int)((a >> 8) & 0xFF) - (int)((b >> 8) & 0xFF), db = (int)((a >> 16) & 0xFF) - (int)((b >> 16) & 0xFF);
        return dr * dr + dg * dg + db * db;
    }

    /// <summary>
    /// The page's pixels again: unchanged ones keep their index; the palette's entries none of
    /// them use take the colours the new pixels miss most (k-means, weighted by how far each is
    /// from the palette), and every new pixel takes its nearest opaque colour.
    /// </summary>
    private static byte[] Index(uint[] canvas, bool[] changed, byte[] pixels, uint[] palette)
    {
        var used = new bool[256];
        for (var i = 0; i < pixels.Length; i++)
        {
            if (!changed[i])
                used[pixels[i]] = true;
        }
        // only opaque colours: the page also has see-through ones (around Chris's hair)
        var kept = Enumerable.Range(0, 256).Where(i => used[i] && palette[i] >> 24 >= 0x80).ToList();
        var free = Enumerable.Range(0, 256).Where(i => !used[i]).ToList();
        var fresh = Enumerable.Range(0, pixels.Length).Where(i => changed[i]).Select(i => canvas[i]).ToList();

        int Nearest(uint c, IEnumerable<int> among) => among.MinBy(i => Distance(palette[i], c));
        if (free.Count != 0 && fresh.Count != 0)
        {
            var misses = fresh.GroupBy(c => c).Select(g => (Color: g.Key, Weight: (double)g.Count() * Distance(palette[Nearest(g.Key, kept)], g.Key)))
                .Where(m => m.Weight > 0).ToList();
            var centres = misses.OrderByDescending(m => m.Weight).Take(free.Count).Select(m => m.Color).ToList();
            for (var round = 0; round < 8 && centres.Count != 0; round++)
            {
                var sums = new double[centres.Count, 4];
                foreach (var (color, weight) in misses)
                {
                    var k = Enumerable.Range(0, centres.Count).MinBy(k => Distance(centres[k], color));
                    sums[k, 0] += (color & 0xFF) * weight;
                    sums[k, 1] += ((color >> 8) & 0xFF) * weight;
                    sums[k, 2] += ((color >> 16) & 0xFF) * weight;
                    sums[k, 3] += weight;
                }
                for (var k = 0; k < centres.Count; k++)
                {
                    if (sums[k, 3] > 0)
                        centres[k] = Rgb((float)(sums[k, 0] / sums[k, 3]), (float)(sums[k, 1] / sums[k, 3]), (float)(sums[k, 2] / sums[k, 3]));
                }
            }
            foreach (var (slot, color) in free.Zip(centres))
            {
                palette[slot] = color;
                kept.Add(slot);
            }
        }

        var result = pixels.ToArray();
        var cache = new Dictionary<uint, byte>();
        for (var i = 0; i < pixels.Length; i++)
        {
            if (!changed[i])
                continue;
            if (!cache.TryGetValue(canvas[i], out var p))
                cache[canvas[i]] = p = (byte)Nearest(canvas[i], kept);
            result[i] = p;
        }
        return result;
    }

    /// <summary>A 5x7 font with the letters of BioRand's characters' names.</summary>
    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['A'] = [".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
        ['B'] = ["####.", "#...#", "#...#", "####.", "#...#", "#...#", "####."],
        ['C'] = [".###.", "#...#", "#....", "#....", "#....", "#...#", ".###."],
        ['D'] = ["####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####."],
        ['E'] = ["#####", "#....", "#....", "####.", "#....", "#....", "#####"],
        ['F'] = ["#####", "#....", "#....", "####.", "#....", "#....", "#...."],
        ['G'] = [".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".####"],
        ['H'] = ["#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
        ['I'] = [".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###."],
        ['J'] = ["..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.."],
        ['K'] = ["#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#"],
        ['L'] = ["#....", "#....", "#....", "#....", "#....", "#....", "#####"],
        ['M'] = ["#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#"],
        ['N'] = ["#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#"],
        ['O'] = [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
        ['P'] = ["####.", "#...#", "#...#", "####.", "#....", "#....", "#...."],
        ['Q'] = [".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#"],
        ['R'] = ["####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#"],
        ['S'] = [".####", "#....", "#....", ".###.", "....#", "....#", "####."],
        ['T'] = ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.."],
        ['U'] = ["#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
        ['V'] = ["#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.."],
        ['W'] = ["#...#", "#...#", "#...#", "#.#.#", "#.#.#", "#.#.#", ".#.#."],
        ['X'] = ["#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#"],
        ['Y'] = ["#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.."],
        ['Z'] = ["#####", "....#", "...#.", "..#..", ".#...", "#....", "#####"],
        ['0'] = [".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###."],
        ['1'] = ["..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###."],
        ['2'] = [".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####"],
        ['3'] = ["#####", "...#.", "..#..", "...#.", "....#", "#...#", ".###."],
        ['4'] = ["...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#."],
        ['5'] = ["#####", "#....", "####.", "....#", "....#", "#...#", ".###."],
        ['6'] = ["..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###."],
        ['7'] = ["#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..."],
        ['8'] = [".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###."],
        ['9'] = [".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.."],
        ['-'] = [".....", ".....", ".....", "#####", ".....", ".....", "....."],
        ['.'] = [".....", ".....", ".....", ".....", ".....", ".##..", ".##.."],
    };
}
