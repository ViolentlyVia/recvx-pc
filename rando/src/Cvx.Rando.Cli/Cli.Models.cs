using System.Buffers.Binary;
using System.Numerics;
using Cvx.Formats;
using Cvx.Formats.Models;
using Cvx.Formats.Rdx;
using Cvx.Rando.Npcs;

namespace Cvx.Rando.Cli;

// Model commands: dump-model, dump-pld, render-model, make-look-mod.
internal static partial class Cli
{
    /// <summary>
    /// A model page and its texture entries, from <c>RM_xxxx:&lt;enemy&gt;</c>,
    /// <c>SYSTEM.AFS:&lt;entry&gt;</c> (a player model) or a file (a page as dump-enemies writes it).
    /// </summary>
    private static (string Name, byte[] Page, IReadOnlyList<byte[]> Textures) LoadModelPage(Options options, string source)
    {
        var colon = source.LastIndexOf(':');
        if (colon > 1 && source[..colon].StartsWith("RM_", StringComparison.OrdinalIgnoreCase))
        {
            var id = RoomId.ParseName(source[..colon]);
            var index = int.Parse(source[(colon + 1)..]);
            using var rooms = RoomArchive.Open(OpenData(options));
            var layout = RdxEnemyLayout.Read(rooms.ReadRoom(id));
            if (index < 0 || index >= layout.Enemies.Count || !layout.Enemies[index].HasModel)
                throw new ArgumentException($"{id.Name} has no enemy {index} with a model.");
            var e = layout.Enemies[index];
            return ($"{id.Name} enemy {index} (type {e.Type})", e.Model.ToArray(), e.Textures.Select(t => layout.TextureGroups[t].ToArray()).ToList());
        }
        if (colon > 1 && source[..colon].EndsWith(".AFS", StringComparison.OrdinalIgnoreCase))
        {
            var archive = source[..colon];
            var path = File.Exists(archive) ? archive : Path.Combine(OpenData(options).Directory, archive);
            using var afs = AfsArchive.Open(path);
            var data = afs.ReadEntry(int.Parse(source[(colon + 1)..]));
            var size = BinaryPrimitives.ReadInt32LittleEndian(data);
            return (source, data.AsSpan(4, size).ToArray(), []);
        }
        var page = File.ReadAllBytes(source);
        // textures written next to it by dump-enemies --out (<stem>.tex0, .tex1...)
        var stem = Path.ChangeExtension(source, null);
        var textures = Enumerable.Range(0, 16).Select(i => $"{stem}.tex{i}").TakeWhile(File.Exists).Select(File.ReadAllBytes).ToList();
        return (source, page, textures);
    }

    private static int DumpModel(Options options)
    {
        var (name, bytes, textures) = LoadModelPage(options, options.Positional(0, "model (RM_xxxx:<enemy>, SYSTEM.AFS:<entry> or a file)"));
        var page = ModelPage.Read(bytes);
        var vertices = options.Has("vertices");
        Console.WriteLine($"{name}: {bytes.Length} bytes, {page.Blocks.Count} blocks, {textures.Count} texture entries");

        SkinData? skin = null;
        var textured = 0;
        foreach (var block in page.Blocks)
        {
            switch (block.Kind)
            {
                case ModelPageBlockKind.Empty:
                    Console.WriteLine("  (empty)");
                    continue;
                case ModelPageBlockKind.Skin:
                    skin = SkinData.Read(block.Data);
                    Console.WriteLine($"  SKIN  {block.Data.Length} bytes ({skin.Words.Length} words)");
                    continue;
                case ModelPageBlockKind.Mask:
                    Console.WriteLine($"  MASK  {block.Data.Length} bytes (face animation)");
                    continue;
            }
            if (block.Model is not { } model)
            {
                Console.WriteLine($"  basic model, {block.Data.Length} bytes (status 0x{block.Data[3]:X2}), not read");
                continue;
            }
            var texture = model.Textures != null && model.Textures.Count != 0 && textured < textures.Count ? textures[textured++] : null;
            DumpNjModel(model, skin, texture, vertices, options.Get("png"), $"{Path.GetFileNameWithoutExtension(name.Split(' ')[0].Replace(':', '_'))}_{textured}");
            skin = null;
        }
        return 0;
    }

    private static void DumpNjModel(NjModel model, SkinData? skin, byte[]? texture, bool vertices, string? pngDir, string pngStem)
    {
        Console.WriteLine($"  model: status 0x{model.Status:X2}, {model.Objects.Count} objects, textures [{string.Join(", ", model.Textures ?? [])}]");
        var parents = model.Parents();
        var bind = model.BindPositions();
        var tree = model.TreeOrder().ToList();
        var depth = new int[model.Objects.Count];
        foreach (var i in tree)
            depth[i] = parents[i] == -1 ? 0 : depth[parents[i]] + 1;
        var entries = skin?.Entries(model).ToDictionary(e => e.Object) ?? [];

        Console.WriteLine("   obj parent tree  eval      position                    bind position               angle          model");
        foreach (var i in Enumerable.Range(0, model.Objects.Count))
        {
            var o = model.Objects[i];
            var line = $"  {i,4} {parents[i],6} {tree.IndexOf(i),4}  {o.EvalFlags:X8}  {V(o.Position)}  {V(bind[i])}  {o.Angle.X,4},{o.Angle.Y,4},{o.Angle.Z,4}";
            if (o.Scale != Vector3.One)
                line += $" scale {V(o.Scale)}";
            if (o.Model is { } m)
            {
                var strips = m.Polygons?.Where(c => c.Kind == NjPolyKind.Strip && c.Type <= NjPolyChunk.StripUvhType).SelectMany(c => c.Strips()).ToList() ?? [];
                var tex = m.Polygons?.Where(c => c.Type == NjPolyChunk.TextureType).Select(c => c.TextureId).Distinct() ?? [];
                var box = Bounds(m);
                line += $"  {m.VertexCount} vertices ({string.Join(",", m.Vertices?.Select(v => v.Type) ?? [])}), {strips.Count} strips, " +
                        $"{strips.Sum(s => s.Vertices.Count - 2)} triangles, textures [{string.Join(",", tex)}], " +
                        $"box {V(box.Min)} to {V(box.Max)}, radius {m.Radius:0.00}";
            }
            if (entries.TryGetValue(i, out var entry))
            {
                line += (entry.Flags & 2) != 0
                    ? $"  SKIN mesh {entry.Mesh}: vertices by object (tree order) {string.Join(" ", entry.Owners.GroupBy(x => x).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}"))}"
                    : $"  SKIN moves mesh {entry.Mesh}";
            }
            Console.WriteLine(new string(' ', 0) + line);
            if (vertices && o.Model?.Vertices is { } chunks)
            {
                foreach (var chunk in chunks.Where(c => c.IsPs2))
                {
                    var n = chunk.IndexOffset;
                    foreach (var (p, nrm) in chunk.Ps2Vertices())
                        Console.WriteLine($"         v{n++,-5} {V(p)}  n {V(nrm)}{(entry is { Owners.Length: > 0 } ? $"  object {entry.Owners[n - 1 - chunk.IndexOffset]} low {entry.Low[n - 1 - chunk.IndexOffset]:X6}" : "")}");
                }
            }
        }

        if (texture != null)
        {
            var entryData = TextureEntry.Read(texture);
            var pictures = entryData.Pictures.ToList();
            Console.WriteLine($"  texture entry: {texture.Length} bytes, {pictures.Count} pictures");
            for (var t = 0; t < pictures.Count; t++)
            {
                var p = pictures[t];
                var label = model.Textures != null && t < model.Textures.Count ? model.Textures[t] : "?";
                Console.WriteLine($"    {t,2} {label,-10} {p.Width}x{p.Height} type {p.ImageType} format {p.PictureFormat} gidx 0x{p.GlobalIndex:X}");
                if (pngDir != null && p.ImageType == 5)
                {
                    Directory.CreateDirectory(pngDir);
                    File.WriteAllBytes(Path.Combine(pngDir, $"{pngStem}_{t:00}_{label}.png"), Png.Encode(p.Width, p.Height, p.ToRgba()));
                }
            }
        }
    }

    private static int DumpPld(Options options)
    {
        // An RE2 PLD (BioRand's characters): skeleton, parts and texture.
        var path = options.Positional(0, "PLD file");
        var pld = Pld.Read(File.ReadAllBytes(path));
        var emr = pld.Armature;
        var rest = emr.RestPositions();
        var parents = emr.Parents();
        var tim = pld.Texture;
        Console.WriteLine($"{path}: {emr.Count} joints, {pld.Mesh.Parts.Count} parts, texture {tim.Width}x{tim.Height} {tim.BitsPerPixel}-bit, {tim.Cluts.Length} CLUTs of {tim.ClutColors} (y {tim.ClutY})");
        Console.WriteLine(" part parent  offset                      rest position               mesh");
        for (var i = 0; i < pld.Mesh.Parts.Count; i++)
        {
            var part = pld.Mesh.Parts[i];
            var line = i < emr.Count ? $"  {i,3} {parents[i],6}  {V(emr.Offsets[i])}  {V(rest[i])}" : $"  {i,3}   (no joint){new string(' ', 51)}";
            if (part.Positions.Length != 0)
            {
                var min = part.Positions.Aggregate(Vector3.Min);
                var max = part.Positions.Aggregate(Vector3.Max);
                var pages = part.Polygons.Select(p => $"{p.Page}/{(p.Clut >> 6) - tim.ClutY}").Distinct().Order();
                line += $"  {part.Positions.Length} vertices, {part.Polygons.Count(p => p.Vertices.Length == 3)} triangles, {part.Polygons.Count(p => p.Vertices.Length == 4)} quads, " +
                        $"box {V(min)} to {V(max)}, page/clut [{string.Join(",", pages)}]";
            }
            Console.WriteLine(line);
            if (options.Has("vertices"))
            {
                for (var v = 0; v < part.Positions.Length; v++)
                    Console.WriteLine($"         v{v,-4} {V(part.Positions[v])}");
            }
        }
        if (options.Get("png") is { } dir)
        {
            Directory.CreateDirectory(dir);
            for (var c = 0; c < tim.Cluts.Length; c++)
            {
                var rgba = new byte[tim.Width * tim.Height * 4];
                for (var y = 0; y < tim.Height; y++)
                {
                    for (var x = 0; x < tim.Width; x++)
                        BinaryPrimitives.WriteUInt32LittleEndian(rgba.AsSpan((y * tim.Width + x) * 4), tim.Rgba(x, y, c));
                }
                File.WriteAllBytes(Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(path)}_clut{c}.png"), Png.Encode(tim.Width, tim.Height, rgba));
            }
        }
        return 0;
    }

    private static int MakeLookMod(Options options)
    {
        // Testing aid: an NPC entry (default: Chris in RM_7220's opening scene) takes a converted
        // RE2 character; writes a mod folder with that room. Not for seeds: Chris stays Chris.
        var pldPath = options.Require("pld");
        var slot = (options.Get("slot") ?? "RM_7220:1").Split(':');
        var id = RoomId.ParseName(slot[0]);
        var index = int.Parse(slot[1]);
        using var rooms = RoomArchive.Open(OpenData(options));
        var file = rooms.ReadRoom(id);
        var layout = RdxEnemyLayout.Read(file);
        if (index < 0 || index >= layout.Enemies.Count || !layout.Enemies[index].HasModel)
            throw new ArgumentException($"{id.Name} has no enemy {index} with a model.");
        var e = layout.Enemies[index];

        // the skeleton: the slot's own model, or --skeleton <RM_xxxx>:<enemy>
        var skeletonSource = options.Get("skeleton") is { } s ? LoadModelPage(options, s) : (id.Name, e.Model.ToArray(), e.Textures.Select(t => layout.TextureGroups[t].ToArray()).ToList());
        var skeleton = ModelPage.Read(skeletonSource.Item2).Models.First();
        var template = skeletonSource.Item3.SelectMany(t => TextureEntry.Read(t).Pictures).FirstOrDefault(p => p.Width == 256 && p.Height == 256 && p.ImageType == 5)
            ?? throw new InvalidDataException("No 256x256 8-bit texture to use as a template.");

        var pld = Pld.Read(File.ReadAllBytes(pldPath));
        var converted = CharacterConverter.Convert(pld, skeleton, template);
        var page = converted.Page.ToBytes();
        var textures = converted.Textures.ToBytes();
        var donor = new RdxEnemyDonor(id, index, e.Type, e.ModelVersion, e.Entry.Effect, (byte)(e.Entry.Index >> 8), page, e.Motion, [textures]);
        var result = RdxEnemyRewriter.Replace(file, new Dictionary<int, RdxEnemyDonor> { [index] = donor });
        var check = RdxEnemyLayout.Read(result);
        var packed = Prs.Compress(result.Data.Span);
        var fits = Expand.FitsRoomPool(packed, out _, out var spare);
        Console.WriteLine($"{id.Name} enemy {index} (type {e.Type}) <- {pldPath}: model {e.Model.Length} -> {page.Length} bytes, " +
                          $"textures {e.Textures.Sum(t => layout.TextureGroups[t].Length)} -> {textures.Length} bytes ({converted.Textures.Pictures.Count()} pictures); " +
                          $"room {file.Length} -> {result.Length} bytes, consistent {check.IsConsistent}, fits {fits} (spare {spare})");
        if (options.Get("dump") is { } dumpDir)
        {
            Directory.CreateDirectory(dumpDir);
            var stem = Path.Combine(dumpDir, Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(pldPath))) ?? "pld");
            File.WriteAllBytes(stem + ".model", page);
            File.WriteAllBytes(stem + ".tex0", textures);
            Console.WriteLine($"Wrote {stem}.model and .tex0");
        }
        if (options.Get("out") is { } outDir)
        {
            Directory.CreateDirectory(outDir);
            rooms.WriteModified(Path.Combine(outDir, GameData.RoomArchiveName), new Dictionary<RoomId, RdxFile> { [id] = result });
            File.WriteAllLines(Path.Combine(outDir, "rando.ini"), ["version=1"]);
            Console.WriteLine($"Wrote {outDir}");
        }
        return check.IsConsistent && fits ? 0 : 1;
    }

    private static int RenderModel(Options options)
    {
        // A model page's first model in its bind pose, textured, from the front, the right side
        // and the back (back faces culled by the strips' winding), to check a conversion by eye.
        var (name, bytes, textures) = LoadModelPage(options, options.Positional(0, "model (RM_xxxx:<enemy> or a file)"));
        var output = options.Require("out");
        var page = ModelPage.Read(bytes);
        var model = page.Models.First();
        var pictures = textures.Count == 0 ? [] : TextureEntry.Read(textures[0]).Pictures.Select(p => (p.Width, p.Height, Rgba: p.ToRgba())).ToList();
        var bind = model.BindPositions();
        var triangles = new List<(Vector3[] P, Vector2[] Uv, int Texture)>();
        for (var o = 0; o < model.Objects.Count; o++)
        {
            if (model.Objects[o].Model is not { Vertices: { } chunks, Polygons: { } polygons } || (model.Objects[o].EvalFlags & NjObject.EvalHide) != 0)
                continue;
            var vertices = new Dictionary<int, Vector3>();
            foreach (var chunk in chunks.Where(c => c.IsPs2))
            {
                var n = chunk.IndexOffset;
                foreach (var (p, _) in chunk.Ps2Vertices())
                    vertices[n++] = bind[o] + p;
            }
            var texture = 0;
            foreach (var c in polygons)
            {
                if (c.Type == NjPolyChunk.TextureType)
                    texture = c.TextureId;
                if (c.Kind != NjPolyKind.Strip || c.Type > NjPolyChunk.StripUvhType)
                    continue;
                foreach (var strip in c.Strips())
                {
                    for (var i = 0; i + 2 < strip.Vertices.Count; i++)
                    {
                        // the game's winding: even triangles (a, b, c), odd ones (a, c, b), all swapped when flipped
                        int[] k = (i % 2 == 1) != strip.Flipped ? [i, i + 2, i + 1] : [i, i + 1, i + 2];
                        triangles.Add((k.Select(j => vertices[strip.Vertices[j].Index]).ToArray(),
                            k.Select(j => new Vector2(strip.Vertices[j].U, strip.Vertices[j].V) / 1024f).ToArray(), texture));
                    }
                }
            }
        }

        const int Width = 300, Height = 400, Views = 3;
        const float Scale = 20;
        var image = new byte[Width * Views * Height * 4];
        for (var i = 0; i < image.Length; i += 4)
            (image[i], image[i + 1], image[i + 2], image[i + 3]) = ((byte)40, (byte)40, (byte)60, (byte)255);
        Func<Vector3, Vector3>[] cameras =
        [
            p => new Vector3(-p.X, p.Y, p.Z),   // front (from -Z)
            p => new Vector3(-p.Z, p.Y, -p.X),  // right side (from +X)
            p => new Vector3(p.X, p.Y, -p.Z),   // back (from +Z)
        ];
        for (var view = 0; view < Views; view++)
        {
            var depth = new float[Width * Height];
            Array.Fill(depth, float.MaxValue);
            foreach (var (points, uvs, texture) in triangles)
            {
                var s = points.Select(p => cameras[view](p)).Select(c => new Vector3(Width / 2 + c.X * Scale, Height - 10 - c.Y * Scale, c.Z)).ToArray();
                var area = (s[1].X - s[0].X) * (s[2].Y - s[0].Y) - (s[2].X - s[0].X) * (s[1].Y - s[0].Y);
                if (area >= 0)
                    continue;
                for (var y = (int)Math.Max(0, s.Min(p => p.Y)); y <= (int)Math.Min(Height - 1, s.Max(p => p.Y)); y++)
                {
                    for (var x = (int)Math.Max(0, s.Min(p => p.X)); x <= (int)Math.Min(Width - 1, s.Max(p => p.X)); x++)
                    {
                        var w0 = ((s[1].X - x) * (s[2].Y - y) - (s[2].X - x) * (s[1].Y - y)) / area;
                        var w1 = ((s[2].X - x) * (s[0].Y - y) - (s[0].X - x) * (s[2].Y - y)) / area;
                        var w2 = 1 - w0 - w1;
                        var z = w0 * s[0].Z + w1 * s[1].Z + w2 * s[2].Z;
                        if (w0 < 0 || w1 < 0 || w2 < 0 || z >= depth[y * Width + x])
                            continue;
                        var color = (R: (byte)200, G: (byte)200, B: (byte)200);
                        if (pictures.Count != 0)
                        {
                            var (tw, th, rgba) = pictures[Math.Min(texture, pictures.Count - 1)];
                            var uv = w0 * uvs[0] + w1 * uvs[1] + w2 * uvs[2];
                            var t = (Math.Clamp((int)(uv.Y * th), 0, th - 1) * tw + Math.Clamp((int)(uv.X * tw), 0, tw - 1)) * 4;
                            if (rgba[t + 3] < 64)
                                continue;
                            color = (rgba[t], rgba[t + 1], rgba[t + 2]);
                        }
                        depth[y * Width + x] = z;
                        var o = (y * Width * Views + view * Width + x) * 4;
                        (image[o], image[o + 1], image[o + 2]) = color;
                    }
                }
            }
        }
        File.WriteAllBytes(output, Png.Encode(Width * Views, Height, image));
        Console.WriteLine($"{name}: {triangles.Count} triangles -> {output}");
        return 0;
    }

    private static (Vector3 Min, Vector3 Max) Bounds(NjChunkModel m)
    {
        var points = m.Vertices?.Where(v => v.IsPs2).SelectMany(v => v.Ps2Vertices()).Select(v => v.Position).ToList() ?? [];
        if (points.Count == 0)
            return default;
        return (points.Aggregate(Vector3.Min), points.Aggregate(Vector3.Max));
    }

    private static string V(Vector3 v) => $"({v.X,7:0.00},{v.Y,7:0.00},{v.Z,7:0.00})";
}
