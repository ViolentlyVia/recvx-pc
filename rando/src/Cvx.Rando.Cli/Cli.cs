using System.Diagnostics;
using System.Text.Json;
using Cvx.Formats;
using Cvx.Formats.Rdx;
using Cvx.Formats.Script;
using Cvx.Rando.Data;
using Cvx.Rando.Items;
using Cvx.Rando.Doors;

namespace Cvx.Rando.Cli;

internal static partial class Cli
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            var options = Options.Parse(args[1..]);
            return args[0] switch
            {
                "list-rooms" => ListRooms(options),
                "dump-room" => DumpRoom(options),
                "dump-all" => DumpAll(options),
                "extract-room" => ExtractRoom(options),
                "extract-afs" => ExtractAfs(options),
                "roundtrip" => Roundtrip(options),
                "make-mod" => MakeMod(options, args[1..]),
                "verify-data" => VerifyData(options),
                "dump-script" => DumpScript(options),
                "generate" => Generate(options),
                "save-rooms" => ListSaveRooms(options),
                "room-memory" => RoomMemory(options),
                "dump-enemies" => DumpEnemies(options),
                "dump-model" => DumpModel(options),
                "dump-pld" => DumpPld(options),
                "make-look-mod" => MakeLookMod(options),
                "render-model" => RenderModel(options),
                "swap-enemies" => SwapEnemies(options),
                "enemy-donors" => EnemyDonors(options),
                "dump-sounds" => DumpSounds(options),
                "dump-music" => DumpMusic(options),
                "bench-prs" => BenchPrs(options),
                _ => Unknown(args[0]),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or InvalidDataException or FormatException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            Usage: cvx-rando-cli <command> [options]

            Commands:
              list-rooms                         every room with its compressed and expanded size
              dump-room <RM_xxxx> [--json]       a room's enemies, items, doors and other trigger areas
              dump-all --out <file.json>         the summary of every room in one JSON file
              extract-room <RM_xxxx> --out <f>   write a room's expanded file
              extract-afs <archive> <entry> --out <f>
                                                 write one entry of an AFS archive as it is
                                                 (the archive's name in the data folder, or a path)
              roundtrip [--out <dir>]            rebuild every room and the archive without changes
                                                 and check the result; --out also writes an
                                                 RDX_LNK.AFS with every room recompressed
              generate [--seed <n>] [--mode shuffle|custom] [--claire-only] [--inventory] [--side-pack] [--no-poison]
                       [--doors] [--segments 1-4] [--segment-size 1-8] [--chris]
                       [--enemies] [--enemy-difficulty 0-3] [--music] [--music-games re2,sh1,...] [--title-call]
                       [--npcs] [--npc-characters leon.re2,ada.re2,...]
                       [--biorand <dir>]
                       [--share <code>] [--out <dir>] [--spoiler]
                                                 randomize (--doors: random doors, played by Claire, or
                                                 Chris with --chris, in 1-4 segments, using 1-8 eighths
                                                 of the rooms); --out writes the mod folder (RDX_LNK.AFS,
                                                 rando.ini, rando_doors.txt, spoiler.txt, graph.dot,
                                                 graph.dgml) for recvx-pc's mod=
              dump-script <RM_xxxx> [--from <hex>] [--to <hex>]
                                                 a room's script opcodes with their file offsets
              swap-enemies <RM_xxxx> --donor <RM_xxxx>:<enemy index> [--only <types>] [--effect <n>] [--out <dir>]
                                                 testing aid: turn a room's enemies into another
                                                 room's enemy and write a mod folder
              dump-enemies [<RM_xxxx>] [--scripts] [--out <dir>]
                                                 each room's enemies with their model, motion and
                                                 texture data; --out writes them as files
              dump-model <RM_xxxx>:<enemy> | <SYSTEM.AFS>:<entry> | <file> [--vertices] [--png <dir>]
                                                 a model page: its blocks, each model's objects
                                                 (skeleton: parent, position, bind position),
                                                 meshes, SKIN weights and textures; --png writes
                                                 the textures
              dump-pld <file.PLD> [--vertices] [--png <dir>]
                                                 an RE2 player model (BioRand's characters):
                                                 skeleton, parts and texture
              render-model <RM_xxxx>:<enemy> | <file> --out <file.png> [--arms-down]
                                                 a model in its bind pose (or with the arms down)
                                                 from the front, the side and the back, textured
                                                 (a file takes the .tex0 next to it)
              make-look-mod --pld <file.PLD> [--slot <RM_xxxx>:<enemy>] [--skeleton <RM_xxxx>:<enemy>]
                            [--dump <dir>] [--out <dir>]
                                                 testing aid: an NPC entry (default RM_7220:1, Chris
                                                 in the opening scene) takes a converted RE2
                                                 character; --out writes a mod folder
              room-memory [--recompress] [--all]
                                                 the rooms with the least room to grow when the game
                                                 expands them (in place, in a fixed pool)
              verify-data [--all]                check the built-in room data against the game's
                                                 rooms; --all also lists info and unmapped rooms
              make-mod --out <dir> [--item <RM_xxxx>:<index>=<item id>]... [--ini <key=value>]...
                                                 a mod folder for recvx-pc (mod= in recvx.ini) with
                                                 hand-made changes, for testing; rando.ini always
                                                 gets version=1

            All commands take --data <dir> (default: CVX_DATA, then ..\recvx-pc\build_pc\data).
            """);
    }

    private static GameData OpenData(Options options)
    {
        var dir = options.Get("data") ?? GameData.FindDefaultDirectory()
            ?? throw new ArgumentException("No game data folder found; pass --data <dir> or set CVX_DATA.");
        return new GameData(dir);
    }

    private static int ListRooms(Options options)
    {
        using var rooms = RoomArchive.Open(OpenData(options));
        Console.WriteLine("index  room     biorand  compressed   expanded");
        for (var i = 0; i < RoomTable.Count; i++)
        {
            var id = RoomTable.Get(i);
            var expanded = rooms.ReadExpanded(id);
            Console.WriteLine($"{i,5}  {id.Name}  {id.BioRandId,7}  {rooms.CompressedSize(id),10}  {expanded.Length,9}");
        }
        return 0;
    }

    private static RoomSummary Summarize(RoomArchive rooms, RoomId id) =>
        RoomSummary.Create(id, rooms.ReadRoom(id), rooms.CompressedSize(id));

    private static int DumpRoom(Options options)
    {
        var id = RoomId.ParseName(options.Positional(0, "room"));
        using var rooms = RoomArchive.Open(OpenData(options));
        var summary = Summarize(rooms, id);
        if (options.Has("json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(summary, Json));
            return 0;
        }

        var c = summary.Counts;
        Console.WriteLine($"{summary.Room} (BioRand {summary.BioRandId})  version {summary.Version}  author {summary.Author}");
        Console.WriteLine($"  {summary.CompressedSize} bytes compressed, {summary.ExpandedSize} expanded");
        Console.WriteLine($"  cameras {c.Cameras}, lights {c.Lights}, objects {c.Objects}, effects {c.Effects}, " +
            $"boundaries {c.Boundaries}, triggers {c.Triggers}, events {c.Events}, messages {c.Messages}");
        Console.WriteLine($"  models {summary.ModelPages} pages, motions {summary.MotionPages} pages, " +
            $"textures {summary.TextureGroups} groups, scripts {summary.ScriptProcedures} procedures " +
            $"({summary.ScriptLength} bytes at 0x{summary.ScriptOffset:X})");

        Console.WriteLine($"Enemies ({summary.Enemies.Count}):");
        foreach (var e in summary.Enemies)
            Console.WriteLine($"  [{e.Index,2}] type {e.Type,3} variant {e.Variant} effect {e.Effect} no {e.Number} flags 0x{e.Flags:X}  at ({e.X:0.#}, {e.Y:0.#}, {e.Z:0.#}) dir {e.Direction}");

        Console.WriteLine($"Items ({summary.Items.Count}):");
        foreach (var i in summary.Items)
            Console.WriteLine($"  [{i.Index,2}] item {i.ItemId,3}  type 0x{i.Type:X4} flags 0x{i.Flags:X} floor {i.Floor} mdl {i.ModelVersion}  at ({i.X:0.#}, {i.Y:0.#}, {i.Z:0.#})");

        Console.WriteLine($"Trigger areas ({summary.Aots.Count}):");
        foreach (var a in summary.Aots)
        {
            var detail = a.Kind switch
            {
                "door" => $"-> {a.DoorTarget} entry {a.Prm2} anim {a.Prm3}",
                "item" => $"item table [{a.Prm0}]",
                _ => $"prm {a.Prm0} {a.Prm1} {a.Prm2} {a.Prm3}",
            };
            Console.WriteLine($"  [{a.Index,2}] {a.Kind,-6} flags 0x{a.Flags:X8}  {detail}  @0x{a.FileOffset:X}");
        }

        Console.WriteLine($"Entrances ({summary.Entrances.Count}):");
        foreach (var p in summary.Entrances)
            Console.WriteLine($"  [{p.Index,2}] ({p.X:0.#}, {p.Y:0.#}, {p.Z:0.#}) dir {p.Direction}");
        return 0;
    }

    private static int DumpAll(Options options)
    {
        var output = options.Require("out");
        using var rooms = RoomArchive.Open(OpenData(options));
        var all = RoomTable.All.Select(id => Summarize(rooms, id)).ToList();
        File.WriteAllText(output, JsonSerializer.Serialize(all, Json));
        Console.WriteLine($"{all.Count} rooms -> {output}");
        return 0;
    }

    private static int ExtractRoom(Options options)
    {
        var id = RoomId.ParseName(options.Positional(0, "room"));
        var output = options.Require("out");
        using var rooms = RoomArchive.Open(OpenData(options));
        var expanded = rooms.ReadExpanded(id);
        File.WriteAllBytes(output, expanded);
        Console.WriteLine($"{id}: {expanded.Length} bytes -> {output}");
        return 0;
    }

    private static int ExtractAfs(Options options)
    {
        var name = options.Positional(0, "archive");
        var index = int.Parse(options.Positional(1, "entry"));
        var output = options.Require("out");
        var path = File.Exists(name) ? name : Path.Combine(OpenData(options).Directory, name);
        using var afs = AfsArchive.Open(path);
        if (index < 0 || index >= afs.Count)
            throw new ArgumentException($"{Path.GetFileName(path)} has entries 0-{afs.Count - 1}.");
        var data = afs.ReadEntry(index);
        File.WriteAllBytes(output, data);
        Console.WriteLine($"{Path.GetFileName(path)} entry {index}: {data.Length} bytes -> {output}");
        return 0;
    }

    private static int Roundtrip(Options options)
    {
        var data = OpenData(options);
        using var rooms = RoomArchive.Open(data);
        var sw = Stopwatch.StartNew();
        var failures = 0;
        long originalTotal = 0, ourTotal = 0;
        var maxGrowth = (Room: default(RoomId), Bytes: long.MinValue);
        var recompressed = new byte[RoomTable.Count][];

        Parallel.For(0, RoomTable.Count, i =>
        {
            var id = RoomTable.Get(i);
            var compressed = rooms.ReadCompressed(id);
            var expanded = Prs.Decompress(compressed);
            var problems = new List<string>();

            var file = new RdxFile(expanded);
            if (file.Header.Version < RdxFile.MinimumVersion)
                problems.Add($"version {file.Header.Version}");

            var rebuilt = file.ToBuilder().ToFile().Data.Span;
            if (!rebuilt.SequenceEqual(expanded))
            {
                var at = FirstDifference(rebuilt, expanded);
                problems.Add($"rebuild differs at 0x{at:X} (length {rebuilt.Length} vs {expanded.Length})");
            }

            var ours = Prs.Compress(expanded);
            if (!Expand.Decompress(ours).AsSpan().SequenceEqual(expanded))
                problems.Add("recompressed room does not expand back to the original");
            recompressed[i] = ours;

            lock (recompressed)
            {
                originalTotal += compressed.Length;
                ourTotal += ours.Length;
                if (ours.Length - compressed.Length > maxGrowth.Bytes)
                    maxGrowth = (id, ours.Length - compressed.Length);
                if (problems.Count != 0)
                {
                    failures++;
                    Console.WriteLine($"{id}: {string.Join("; ", problems)}");
                }
            }
        });

        Console.WriteLine($"Rooms: {RoomTable.Count - failures}/{RoomTable.Count} OK ({sw.Elapsed.TotalSeconds:0.0}s)");
        Console.WriteLine($"Compressed total: original {originalTotal:N0}, ours {ourTotal:N0} ({100.0 * ourTotal / originalTotal:0.0}%)");
        Console.WriteLine($"Largest growth: {maxGrowth.Room} {maxGrowth.Bytes:+#,0;-#,0;0} bytes");

        // The archive itself: rewriting it with nothing replaced must give the same bytes.
        using (var copy = new MemoryStream())
        using (var original = File.OpenRead(data.RoomArchivePath))
        {
            AfsWriter.Write(rooms.Afs, new Dictionary<int, byte[]>(), copy);
            copy.Position = 0;
            var same = StreamsEqual(copy, original);
            Console.WriteLine($"Archive rewrite: {(same ? "identical" : "DIFFERENT")}");
            if (!same)
                failures++;
        }

        if (options.Get("out") is { } outDir)
        {
            Directory.CreateDirectory(outDir);
            var path = Path.Combine(outDir, GameData.RoomArchiveName);
            var replacements = Enumerable.Range(0, RoomTable.Count).ToDictionary(i => i, i => recompressed[i]);
            AfsWriter.Write(rooms.Afs, replacements, path);
            using var check = AfsArchive.Open(path);
            for (var i = 0; i < check.Count; i++)
            {
                if (!Prs.Decompress(check.ReadEntry(i)).AsSpan().SequenceEqual(rooms.ReadExpanded(RoomTable.Get(i))))
                {
                    Console.WriteLine($"{RoomTable.Get(i)}: differs in the written archive");
                    failures++;
                }
            }
            Console.WriteLine($"Wrote {path} ({new FileInfo(path).Length:N0} bytes, every room recompressed)");
        }

        return failures == 0 ? 0 : 1;
    }

    private static int DumpEnemies(Options options)
    {
        // Each room's enemies with the model, motion and texture data the game loads for them.
        using var rooms = RoomArchive.Open(OpenData(options));
        var only = options.OptionalPositional(0) is { } name ? RoomId.ParseName(name) : (RoomId?)null;
        var bad = 0;
        foreach (var id in RoomTable.All.Where(r => only == null || r == only))
        {
            var layout = RdxEnemyLayout.Read(rooms.ReadRoom(id));
            if (!layout.IsConsistent)
                bad++;
            if (layout.Enemies.Count == 0 && only == null && layout.IsConsistent)
                continue;
            Console.WriteLine($"{id.Name} ({id.BioRandId}): {layout.Enemies.Count} enemies, textures {layout.RoomTextures}+{layout.EnemyTextures}+{layout.OtherTextures}" +
                              $"/{layout.TextureGroups.Length}, motions {layout.MotionEntries.Length}{(layout.IsConsistent ? "" : "  INCONSISTENT")}");
            if (options.Has("scripts"))
            {
                // script opcodes that act on enemies, with their counts
                var ops = CvScriptReader.ReadOpcodes(rooms.ReadRoom(id))
                    .Where(o => o.Name.Contains("enemy") || o.Name.Contains("death") || o.Name.Contains("ene_"))
                    .GroupBy(o => o.Name).Select(g => $"{g.Key}:{g.Count()}");
                Console.WriteLine($"  scripts: {string.Join(" ", ops)}");
            }
            foreach (var e in layout.Enemies)
            {
                Console.WriteLine($"  [{e.Index,2}] type {e.Type,3} mdlver {e.ModelVersion} flg {e.Entry.Header:X} fx {e.Entry.Effect} idx {e.Entry.Index:X4}" +
                                  $"  model {e.Model.Length,7}  motion {e.Motion.Length,7}  textures [{string.Join(",", e.Textures.Select(t => $"{t}:{layout.TextureGroups[t].Length}"))}]");
                if (options.Get("out") is { } dir)
                {
                    // <room>_<enemy>.model, .motion and .tex<n> (each texture entry), for format work
                    Directory.CreateDirectory(dir);
                    var stem = Path.Combine(dir, $"{id.Name}_{e.Index}");
                    File.WriteAllBytes(stem + ".model", e.Model.ToArray());
                    if (e.Motion.Length != 0)
                        File.WriteAllBytes(stem + ".motion", e.Motion.ToArray());
                    for (var t = 0; t < e.Textures.Count; t++)
                        File.WriteAllBytes(stem + $".tex{t}", layout.TextureGroups[e.Textures[t]].ToArray());
                }
            }
        }
        if (only == null)
            Console.WriteLine($"{bad} rooms not understood");
        return bad == 0 ? 0 : 1;
    }

    private static int DumpMusic(Options options)
    {
        // Every music track: which discs have it, its length, whether it loops, and which rooms ask for it.
        var data = OpenData(options);
        var discs = new[] { 1, 2 }.ToDictionary(d => d, d => global::Cvx.Formats.Music.ReadDisc(data, d));
        using var rooms = RoomArchive.Open(data);
        var requests = RoomTable.All.SelectMany(id => global::Cvx.Formats.Music.Requests(id, rooms.ReadRoom(id))).ToList();
        var tracks = discs.Values.SelectMany(d => d.Keys).Concat(requests.Select(r => r.Track)).Distinct().Order();
        foreach (var t in tracks)
        {
            var info = discs[1].TryGetValue(t, out var a) ? a : discs[2].TryGetValue(t, out var b) ? b : (AdxInfo?)null;
            var where = string.Join("", new[] { 1, 2 }.Select(d => discs[d].ContainsKey(t) ? d.ToString() : "-"));
            var uses = requests.Where(r => r.Track == t).ToList();
            var rs = string.Join(" ", uses.GroupBy(r => r.Room).Select(g => $"{g.Key.Name}{(g.All(r => r.Opcode == "set_next_room_bgm") ? "n" : "")}"));
            Console.WriteLine($"{t,3} disc {where} {(info is { } i ? $"{i.Seconds,6:0.0}s {(i.Loops ? "loop" : "once")} {i.Channels}ch {i.SampleRate}" : "   (none)"),-28} {uses.Count,3} uses: {rs}");
        }

        // --levels: each track's loudness (RMS and peak, decoded), and the average of the
        // tracks the shuffle uses: data/music.json's targetRms, which other games' music is matched to
        if (options.Has("levels"))
        {
            var catalog = RandoData.Load().Music;
            var levels = new List<double>();
            foreach (var t in catalog.Categories.SelectMany(c => c.Tracks).Order())
            {
                var disc = discs[1].ContainsKey(t) ? 1 : 2;
                using var afs = AfsArchive.Open(Path.Combine(data.Directory, $"BGM{disc}.AFS"));
                var (pcm, _) = Adx.Decode(afs.ReadEntry(t));
                var (rms, peak) = global::Cvx.Rando.Music.MusicLibrary.Level(pcm);
                levels.Add(rms);
                Console.WriteLine($"{t,3} rms {rms,7:0} peak {peak,5}");
            }
            Console.WriteLine($"average rms {levels.Average():0}");
        }
        return 0;
    }

    private static int DumpSounds(Options options)
    {
        // A room's sound pack: its banks (type, number, size).
        var data = OpenData(options);
        foreach (var disc in new[] { 1, 2 })
        {
            using var packs = SoundPacks.Open(data, disc);
            foreach (var name in options.Positional(0, "room").Split(','))
            {
                var id = RoomId.ParseName(name);
                if (packs.ReadRoom(id) is not { } pack)
                    continue;
                var banks = SoundPacks.Parse(pack);
                Console.WriteLine($"{packs.Name} {id.Name}: {pack.Length} bytes, rebuilt the same: {SoundPacks.Build(banks).AsSpan().SequenceEqual(pack)}");
                foreach (var b in banks)
                    Console.WriteLine($"  type {b.Type} bank {b.BankNo,2}  {b.Data.Length,8}  {Convert.ToHexString(b.Data.Span[..Math.Min(16, b.Data.Length)])}");
            }
        }
        return 0;
    }

    private static int EnemyDonors(Options options)
    {
        // For each enemy type and model version: the rooms an enemy can be copied from.
        using var rooms = RoomArchive.Open(OpenData(options));
        var found = new SortedDictionary<(int, int), List<string>>();
        foreach (var id in RoomTable.All)
        {
            var file = rooms.ReadRoom(id);
            var layout = RdxEnemyLayout.Read(file);
            foreach (var e in layout.Enemies.Where(e => e.Type is > 0 and <= 30))
            {
                if (RdxEnemyDonor.From(id, file, e.Index) is not { } d)
                    continue;
                if (!found.TryGetValue((d.Type, d.ModelVersion), out var list))
                    found[(d.Type, d.ModelVersion)] = list = [];
                list.Add($"{id.Name}#{e.Index}(fx{e.Entry.Effect},{(d.Model.Length + d.Motion.Length + d.Textures.Sum(t => t.Length)) / 1024}K)");
            }
        }
        foreach (var ((type, version), list) in found)
            Console.WriteLine($"type {type,2} v{version,-2}: {string.Join(" ", list.Take(6))}");
        return 0;
    }

    private static int SwapEnemies(Options options)
    {
        // Testing aid: every enemy of a room (or --only <types>) becomes a donor enemy.
        using var rooms = RoomArchive.Open(OpenData(options));
        var id = RoomId.ParseName(options.Positional(0, "room"));
        var donorSpec = options.Require("donor").Split(':');
        var donorRoom = RoomId.ParseName(donorSpec[0]);
        var donor = RdxEnemyDonor.From(donorRoom, rooms.ReadRoom(donorRoom), int.Parse(donorSpec[1]),
            options.Get("effect") is { } fx ? short.Parse(fx) : null)
            ?? throw new ArgumentException($"{options.Get("donor")} cannot be a donor.");
        var only = options.Get("only")?.Split(',').Select(int.Parse).ToHashSet();
        var file = rooms.ReadRoom(id);
        var targets = RdxEnemyLayout.Read(file).Enemies.Where(e => only == null || only.Contains(e.Type))
            .ToDictionary(e => e.Index, _ => donor);
        var result = RdxEnemyRewriter.Replace(file, targets);
        var check = RdxEnemyLayout.Read(result);
        var packed = Prs.Compress(result.Data.Span);
        var fits = Expand.FitsRoomPool(packed, out var size, out var spare);
        Console.WriteLine($"{id.Name}: {targets.Count} enemies -> type {donor.Type} version {donor.ModelVersion} from {donorRoom.Name}#{donor.Index}; " +
                          $"{file.Length} -> {result.Length} bytes, consistent {check.IsConsistent}, fits {fits} (spare {spare})");
        if (options.Get("out") is { } outDir)
        {
            Directory.CreateDirectory(outDir);
            rooms.WriteModified(Path.Combine(outDir, GameData.RoomArchiveName), new Dictionary<RoomId, RdxFile> { [id] = result });
            Console.WriteLine($"Wrote {outDir}");
        }
        return check.IsConsistent && fits ? 0 : 1;
    }

    private static int RoomMemory(Options options)
    {
        // How much each room can grow: recvx-pc expands a room in place inside a fixed pool.
        using var rooms = RoomArchive.Open(OpenData(options));
        var recompress = options.Has("recompress");
        var results = new (RoomId Id, int Size, int Packed, int Spare, bool Fits)[RoomTable.Count];
        Parallel.For(0, RoomTable.Count, i =>
        {
            var id = RoomTable.Get(i);
            var packed = recompress ? Prs.Compress(rooms.ReadExpanded(id)) : rooms.ReadCompressed(id);
            var fits = Expand.FitsRoomPool(packed, out var size, out var spare);
            results[i] = (id, size, packed.Length, spare, fits);
        });
        Console.WriteLine($"room     expanded    packed     spare  (pool {Expand.RoomPool})");
        foreach (var r in results.OrderBy(r => r.Spare).Take(options.Has("all") ? RoomTable.Count : 15))
            Console.WriteLine($"{r.Id.Name}  {r.Size,9}  {r.Packed,8}  {r.Spare,8}{(r.Fits ? "" : "  DOES NOT FIT")}");
        return results.All(r => r.Fits) ? 0 : 1;
    }

    private static int ListSaveRooms(Options options)
    {
        using var rooms = RoomArchive.Open(OpenData(options));
        foreach (var (id, info) in SaveRooms.FindAll(rooms).OrderBy(x => x.Key))
        {
            if (info.ItemBox || info.Typewriter)
                Console.WriteLine($"{id.Name} ({id.BioRandId}): {(info.ItemBox ? "item box" : "")}{(info.ItemBox && info.Typewriter ? " + " : "")}{(info.Typewriter ? "typewriter" : "")}");
        }
        return 0;
    }

    private static int Generate(Options options)
    {
        var seed = options.Get("seed") is { } s ? int.Parse(s) : Random.Shared.Next(0, 1_000_000);
        var settings = new RandoSettings
        {
            Seed = seed,
            ItemMode = options.Get("mode")?.ToLowerInvariant() == "custom" ? ItemMode.Custom : ItemMode.Shuffle,
            IncludeChris = !options.Has("claire-only"),
            RandomInventory = options.Has("inventory"),
            NoNosferatuPoison = options.Has("no-poison"),
            StartSidePack = options.Has("side-pack"),
            RandomDoors = options.Has("doors"),
            DoorSegments = options.Get("segments") is { } sg ? int.Parse(sg) : 4,
            DoorSegmentSize = options.Get("segment-size") is { } sz ? int.Parse(sz) : 8,
            DoorCharacter = options.Has("chris") ? Character.Chris : Character.Claire,
            RandomEnemies = options.Has("enemies"),
            RandomMusic = options.Has("music"),
            RandomTitleCall = options.Has("title-call"),
            RandomNpcs = options.Has("npcs"),
            MusicGames = options.Get("music-games") is { } mg ? ["recv", .. mg.Split(',', StringSplitOptions.RemoveEmptyEntries)] : ["recv"],
            NpcCharacters = options.Get("npc-characters") is { } nc ? nc.Split(',', StringSplitOptions.RemoveEmptyEntries) : [],
            EnemyDifficulty = options.Get("enemy-difficulty") is { } ed ? int.Parse(ed) : 1,
        };
        if (options.Get("share") is { } code)
        {
            // a share code from the app sets everything
            if (!Cvx.Rando.Setup.ShareCode.TryDecode(code, out var shared, out var error))
                throw new ArgumentException(error);
            settings = shared!;
            seed = settings.Seed;
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(settings));
        }
        using var rooms = RoomArchive.Open(OpenData(options));
        var sw = Stopwatch.StartNew();
        GenerationResult result;
        try
        {
            var library = Cvx.Rando.Music.MusicLibrary.Find(options.Get("biorand"), AppContext.BaseDirectory, Environment.CurrentDirectory);
            if ((settings.RandomMusic && settings.MusicGames.Any(g => g != "recv")) || settings.RandomTitleCall)
                Console.WriteLine(library == null ? "No BioRand music found (--biorand <folder>)." : $"BioRand music: {library.Directory}, {library.Tracks.Count} tracks");
            var characters = settings.RandomNpcs && settings.NpcCharacters.Length != 0
                ? Cvx.Rando.Npcs.CharacterLibrary.Find(options.Get("biorand"), AppContext.BaseDirectory, Environment.CurrentDirectory)
                : null;
            if (settings.RandomNpcs && settings.NpcCharacters.Length != 0)
                Console.WriteLine(characters == null ? "No BioRand characters found (--biorand <folder>)." : $"BioRand characters: {characters.Directory}, {characters.Characters.Count}");
            result = Generator.Generate(settings, RandoData.Load(), rooms, null, library, characters);
        }
        catch (ItemPlacementException ex)
        {
            Console.Error.WriteLine($"seed {seed}: {ex.Message}");
            return 2;
        }
        Console.WriteLine($"Seed {seed}: {result.Patches.Count} patches in {result.ChangedRooms.Count} rooms" +
                          $"{(result.Doors != null ? $", door layout {result.DoorAttempts}" : "")}" +
                          $"{(result.Settings.RandomEnemies ? $", enemies changed in {result.Enemies.Count} rooms" : "")} ({sw.Elapsed.TotalSeconds:0.0}s)");
        if (options.Has("spoiler"))
            Console.WriteLine(result.Spoiler);
        if (options.Get("out") is { } outDir)
        {
            sw.Restart();
            Generator.WriteMod(result, rooms, outDir, new Progress<string>(Console.WriteLine));
            Console.WriteLine($"Wrote {Path.GetFullPath(outDir)} ({sw.Elapsed.TotalSeconds:0.0}s)");
        }
        return 0;
    }

    private static int DumpScript(Options options)
    {
        var id = RoomId.ParseName(options.Positional(0, "room"));
        int ParseHex(string? s, int def) => s == null ? def : Convert.ToInt32(s.StartsWith("0x") ? s[2..] : s, 16);
        var from = ParseHex(options.Get("from"), 0);
        var to = ParseHex(options.Get("to"), int.MaxValue);
        using var rooms = RoomArchive.Open(OpenData(options));
        var file = rooms.ReadRoom(id);
        var items = RandoData.Load().Items;
        var procedure = -1;
        foreach (var op in CvScriptReader.ReadOpcodes(file))
        {
            if (op.Offset < from || op.Offset > to)
                continue;
            if (op.Procedure != procedure)
            {
                procedure = op.Procedure;
                Console.WriteLine($"procedure {procedure}:");
            }
            var note = "";
            var b = op.Bytes.Span;
            var positions = CvOpcodes.ItemPositions(op);
            if (positions.Length != 0)
                note = "  ; " + string.Join(", ", positions.Select(p => items.NameOf(op.Bytes.Span[p])));
            else if (op.Opcode == CvOpcodes.SetDoorCall)
                note = $"  ; door to stage {b[4]} room {b[5]} entrance {b[6]} (RM_{b[4]}{b[5]:00}x)";
            else if (op.Opcode == CvOpcodes.SetEtcAtari2)
                note = $"  ; trigger area {b[1]} type {b[8]} prm {b[4]} {b[5]} {b[6]} {b[7]}";
            Console.WriteLine($"  0x{op.Offset:X6}  {op.Name,-24} {Convert.ToHexString(b)}{note}");
        }
        return 0;
    }

    private static int VerifyData(Options options)
    {
        using var rooms = RoomArchive.Open(OpenData(options));
        var sw = Stopwatch.StartNew();
        var report = new DataValidator(RandoData.Load(), rooms).Validate();
        var all = options.Has("all");
        foreach (var issue in report.Issues.Where(x => all || x.Severity != IssueSeverity.Info))
            Console.WriteLine(issue);

        Console.WriteLine();
        Console.WriteLine($"Checked {report.RoomsChecked} rooms, {report.DoorsChecked} doors, {report.ItemsChecked} items, " +
            $"{report.OffsetsChecked} script offsets ({sw.Elapsed.TotalSeconds:0.0}s): " +
            $"{report.Errors.Count()} errors, {report.Warnings.Count()} warnings");
        Console.WriteLine($"{report.UnmappedRooms.Count} rooms are not in the room data" +
            (all ? ":" : $"; {report.UnmappedRooms.Count(x => x.ItemPickups > 0 || x.Doors > 0)} of them have doors or pickups (--all lists them)"));
        if (all)
        {
            foreach (var u in report.UnmappedRooms)
                Console.WriteLine($"  {u.Room.Name} ({u.Room.BioRandId}): {u.Doors} doors, {u.ItemPickups} pickups, {u.Items} items, {u.Enemies} enemies, {u.Entrances} entrances");
        }
        return report.Errors.Any() ? 1 : 0;
    }

    private static int MakeMod(Options options, string[] args)
    {
        var outDir = options.Require("out");
        var edits = new List<(RoomId Room, int Index, int ItemId)>();
        var ini = new List<string> { "version=1" };
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--item")
            {
                // RM_7110:1=12
                var spec = args[++i];
                var eq = spec.Split('=', 2);
                var left = eq[0].Split(':', 2);
                if (eq.Length != 2 || left.Length != 2)
                    throw new ArgumentException($"--item expects <room>:<index>=<item id>, got {spec}");
                edits.Add((RoomId.ParseName(left[0]), int.Parse(left[1]), int.Parse(eq[1])));
            }
            else if (args[i] == "--ini")
            {
                ini.Add(args[++i]);
            }
        }

        using var rooms = RoomArchive.Open(OpenData(options));
        var changed = new Dictionary<RoomId, RdxFile>();
        foreach (var (room, index, itemId) in edits)
        {
            var file = changed.TryGetValue(room, out var f) ? f : rooms.ReadRoom(room);
            if (index < 0 || index >= file.Items.Length)
                throw new ArgumentException($"{room} has {file.Items.Length} items, no item {index}.");
            var item = file.Items[index];
            Console.WriteLine($"{room} item {index}: {item.ItemId} -> {itemId}");
            item.ItemId = (ushort)itemId;
            changed[room] = file.WithRecord(RdxTable.Items, index, item);
        }

        Directory.CreateDirectory(outDir);
        var afsPath = Path.Combine(outDir, GameData.RoomArchiveName);
        var sw = Stopwatch.StartNew();
        rooms.WriteModified(afsPath, changed);
        Console.WriteLine($"Wrote {afsPath} ({changed.Count} room(s) changed, {sw.Elapsed.TotalSeconds:0.0}s)");

        File.WriteAllLines(Path.Combine(outDir, "rando.ini"), ini);
        Console.WriteLine($"Wrote rando.ini: {string.Join(", ", ini)}");
        return 0;
    }

    // Development aid: compares compressor settings on a few rooms.
    private static int BenchPrs(Options options)
    {
        using var rooms = RoomArchive.Open(OpenData(options));
        var ids = (options.Get("rooms") ?? "RM_7100,RM_0030,RM_9350,RM_0100,RM_1030")
            .Split(',').Select(RoomId.ParseName).ToArray();
        var inputs = ids.Select(id => (id, rooms.ReadExpanded(id), rooms.CompressedSize(id))).ToArray();
        Console.WriteLine($"original total {inputs.Sum(x => (long)x.Item3):N0}");
        foreach (var (lazy, chain) in new[] { (false, 512), (true, 64), (true, 128), (true, 512), (true, 2048) })
        {
            var sw = Stopwatch.StartNew();
            long total = 0;
            foreach (var (id, data, _) in inputs)
                total += Prs.Compress(data, lazy, chain).Length;
            Console.WriteLine($"lazy={lazy,-5} chain={chain,5}: {total,12:N0}  {sw.Elapsed.TotalSeconds,5:0.0}s");
        }
        return 0;
    }

    private static int FirstDifference(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++)
        {
            if (a[i] != b[i])
                return i;
        }
        return n;
    }

    private static bool StreamsEqual(Stream a, Stream b)
    {
        if (a.Length != b.Length)
            return false;
        var ba = new byte[1 << 20];
        var bb = new byte[1 << 20];
        while (true)
        {
            var na = a.ReadAtLeast(ba, ba.Length, throwOnEndOfStream: false);
            var nb = b.ReadAtLeast(bb, bb.Length, throwOnEndOfStream: false);
            if (na != nb || !ba.AsSpan(0, na).SequenceEqual(bb.AsSpan(0, nb)))
                return false;
            if (na == 0)
                return true;
        }
    }
}

internal sealed class Options
{
    private readonly Dictionary<string, string?> _named = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _positional = [];

    public static Options Parse(string[] args)
    {
        var result = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var name = arg[2..];
                string? value = null;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    value = args[++i];
                result._named[name] = value;
            }
            else
            {
                result._positional.Add(arg);
            }
        }
        return result;
    }

    public string? Get(string name) => _named.TryGetValue(name, out var v) ? v : null;
    public bool Has(string name) => _named.ContainsKey(name);
    public string Require(string name) => Get(name) ?? throw new ArgumentException($"--{name} is required.");

    public string? OptionalPositional(int index) => index < _positional.Count ? _positional[index] : null;

    public string Positional(int index, string what) =>
        index < _positional.Count ? _positional[index] : throw new ArgumentException($"Missing {what}.");
}
