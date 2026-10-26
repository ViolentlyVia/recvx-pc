// The title picture after BioRand's BiorandBgCreator and ReCvRandomiser.ReplaceBackground (MIT,
// Copyright (c) 2022-2026 Ted John): BioRand's CV:X title art (data/recv/bg.png) with the version
// in the top left corner. BioRand's QR code of the seed is left out: drawn as large as a long
// share code needs, it covered the art (owner's look in the game).

using System.IO.Compression;
using System.Reflection;
using Cvx.Formats;
using Cvx.Formats.Models;

namespace Cvx.Rando.Title;

/// <summary>
/// The title screen of a randomized game. The game draws its title from ADV.AFS entry 2
/// (adv.c: <c>RequestAdvInsideFileEx(2, 0)</c>, <c>DisplayTitleBg</c>): resource 0 is the
/// background, a 1024x512 8-bit TIM2 picture at 0x460 whose top left 640x448 is the screen
/// (the Resident Evil Code: Veronica X logo is part of it), after a Dreamcast palette (PVPL) the
/// game reads only its bank number from. A seed's title is BioRand's CV:X art, which adds
/// BIORAND under the logo, with "Biorand++ 0.9.94 (commit)" in the top left corner.
/// </summary>
public static class TitleScreen
{
    public const string Archive = "ADV.AFS";
    public const int Entry = 2;
    /// <summary>Where BioRand keeps the art, in its data folder or a datapack.</summary>
    public const string BackgroundPath = "data/recv/bg.png";

    private const int PictureStart = 0x460;
    private const int PictureEnd = 0x80960;
    private const int Width = 1024;
    private const int Height = 512;
    /// <summary>BioRand's margin for a 1024-wide picture, and the caption's pixels a dot.</summary>
    private const int Margin = 16;
    private const int TextScale = 3;

    /// <summary>"Biorand++ 0.9.94 (b6da6e9)", from the program's version and commit.</summary>
    public static string Caption
    {
        get
        {
            var info = typeof(TitleScreen).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
            var plus = info.IndexOf('+');
            var version = plus < 0 ? info : info[..plus];
            var commit = plus < 0 ? "" : info[(plus + 1)..];
            return commit.Length >= 7 ? $"Biorand++ {version} ({commit[..7]})" : $"Biorand++ {version}";
        }
    }

    /// <summary>BioRand's CV:X title art from its folder (data first, then the datapacks), or null.</summary>
    public static byte[]? FindBackground(string? bioRandDirectory)
    {
        if (string.IsNullOrEmpty(bioRandDirectory) || !Directory.Exists(bioRandDirectory))
            return null;
        var loose = Path.Combine(bioRandDirectory, BackgroundPath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(loose))
            return File.ReadAllBytes(loose);
        var packs = Path.Combine(bioRandDirectory, "datapacks");
        if (!Directory.Exists(packs))
            return null;
        foreach (var zip in Directory.EnumerateFiles(packs, "*.zip").Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var archive = ZipFile.OpenRead(zip);
                var entry = archive.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').Equals(BackgroundPath, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                    continue;
                using var stream = entry.Open();
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                // a broken or locked pack: try the next
            }
        }
        return null;
    }

    /// <summary>ADV.AFS entry 2 with <paramref name="backgroundPng"/> (1024x512) as the title and the caption on it.</summary>
    public static byte[] Make(ReadOnlySpan<byte> entry, ReadOnlySpan<byte> backgroundPng, string caption)
    {
        var picture = new Tim2Picture(entry[PictureStart..PictureEnd].ToArray());
        if (picture.ImageType != 5 || picture.Width != Width || picture.Height != Height)
            throw new InvalidDataException($"ADV.AFS entry 2: the title picture is {picture.Width}x{picture.Height} type {picture.ImageType}, not 1024x512 8-bit.");
        var (w, h, rgba) = Png.Decode(backgroundPng);
        if (w != Width || h != Height)
            throw new InvalidDataException($"{BackgroundPath} is {w}x{h}, not {Width}x{Height}.");

        var canvas = new uint[Width * Height];
        for (var i = 0; i < canvas.Length; i++)
            canvas[i] = rgba[i * 4] | (uint)rgba[i * 4 + 1] << 8 | (uint)rgba[i * 4 + 2] << 16 | 0xFF000000;
        DrawText(canvas, caption, Margin, Margin);

        var (pixels, palette) = Index(canvas);
        var made = Tim2Picture.Create(picture, picture.GlobalIndex, Width, Height, pixels, palette);
        var result = entry.ToArray();
        made.Data.CopyTo(result, PictureStart);
        return result;
    }

    // ---------------------------------------------------------------- drawing

    private const uint White = 0xFFFFFFFF;
    private const uint Black = 0xFF000000;

    /// <summary>White text with a black shadow, <see cref="TextScale"/> pixels a dot.</summary>
    private static void DrawText(uint[] canvas, string text, int left, int top)
    {
        foreach (var (dx, color) in new[] { (TextScale / 2 + 1, Black), (0, White) })
        {
            var x = left + dx;
            foreach (var c in text)
            {
                if (Glyphs.TryGetValue(c, out var rows))
                {
                    for (var row = 0; row < rows.Length; row++)
                    {
                        for (var col = 0; col < rows[row].Length; col++)
                        {
                            if (rows[row][col] == '#')
                                Fill(canvas, x + col * TextScale, top + dx + row * TextScale, TextScale, TextScale, color);
                        }
                    }
                }
                x += 6 * TextScale;
            }
        }
    }

    private static void Fill(uint[] canvas, int x, int y, int w, int h, uint color)
    {
        for (var yy = Math.Max(0, y); yy < Math.Min(Height, y + h); yy++)
        {
            for (var xx = Math.Max(0, x); xx < Math.Min(Width, x + w); xx++)
                canvas[yy * Width + xx] = color;
        }
    }

    /// <summary>
    /// The picture as 8-bit indexes and 256 colours. The art has 256 colours or fewer; when the
    /// caption makes more, the least used colours give way to the nearest kept one.
    /// </summary>
    private static (byte[] Pixels, uint[] Palette) Index(uint[] canvas)
    {
        var counts = new Dictionary<uint, int>();
        foreach (var c in canvas)
            counts[c] = counts.GetValueOrDefault(c) + 1;
        var kept = counts.OrderByDescending(x => x.Key is White or Black ? int.MaxValue : x.Value).ThenBy(x => x.Key)
            .Take(256).Select(x => x.Key).ToList();
        var index = kept.Select((c, i) => (c, i)).ToDictionary(x => x.c, x => (byte)x.i);
        var pixels = new byte[canvas.Length];
        for (var i = 0; i < canvas.Length; i++)
        {
            if (!index.TryGetValue(canvas[i], out var p))
                index[canvas[i]] = p = (byte)kept.Select((k, j) => (Distance(k, canvas[i]), j)).Min().j;
            pixels[i] = p;
        }
        var palette = new uint[256];
        for (var i = 0; i < kept.Count; i++)
            palette[i] = kept[i];   // (the game's own title palette has full alpha too)
        return (pixels, palette);
    }

    private static int Distance(uint a, uint b)
    {
        int dr = (int)(a & 0xFF) - (int)(b & 0xFF), dg = (int)((a >> 8) & 0xFF) - (int)((b >> 8) & 0xFF), db = (int)((a >> 16) & 0xFF) - (int)((b >> 16) & 0xFF);
        return dr * dr + dg * dg + db * db;
    }

    /// <summary>A 5x7 font with what the caption uses (the version and a commit's hex digits).</summary>
    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['B'] = ["####.", "#...#", "#...#", "####.", "#...#", "#...#", "####."],
        ['i'] = ["..#..", ".....", ".##..", "..#..", "..#..", "..#..", ".###."],
        ['o'] = [".....", ".....", ".###.", "#...#", "#...#", "#...#", ".###."],
        ['r'] = [".....", ".....", "#.##.", "##..#", "#....", "#....", "#...."],
        ['a'] = [".....", ".....", ".###.", "....#", ".####", "#...#", ".####"],
        ['n'] = [".....", ".....", "#.##.", "##..#", "#...#", "#...#", "#...#"],
        ['d'] = ["....#", "....#", ".##.#", "#..##", "#...#", "#...#", ".####"],
        ['b'] = ["#....", "#....", "#.##.", "##..#", "#...#", "#...#", "####."],
        ['c'] = [".....", ".....", ".###.", "#....", "#....", "#...#", ".###."],
        ['e'] = [".....", ".....", ".###.", "#...#", "#####", "#....", ".###."],
        ['f'] = ["..##.", ".#..#", ".#...", "###..", ".#...", ".#...", ".#..."],
        ['+'] = [".....", "..#..", "..#..", "#####", "..#..", "..#..", "....."],
        ['-'] = [".....", ".....", ".....", "#####", ".....", ".....", "....."],
        ['.'] = [".....", ".....", ".....", ".....", ".....", ".##..", ".##.."],
        ['('] = ["...#.", "..#..", ".#...", ".#...", ".#...", "..#..", "...#."],
        [')'] = [".#...", "..#..", "...#.", "...#.", "...#.", "..#..", ".#..."],
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
    };
}
