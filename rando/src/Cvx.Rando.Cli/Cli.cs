using System.Diagnostics;
using System.Text.Json;
using Cvx.Formats;
using Cvx.Formats.Rdx;
using Cvx.Formats.Script;
using Cvx.Rando.Data;

namespace Cvx.Rando.Cli;

internal static class Cli
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
                "roundtrip" => Roundtrip(options),
                "make-mod" => MakeMod(options, args[1..]),
                "verify-data" => VerifyData(options),
                "dump-script" => DumpScript(options),
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
              roundtrip [--out <dir>]            rebuild every room and the archive without changes
                                                 and check the result; --out also writes an
                                                 RDX_LNK.AFS with every room recompressed
              dump-script <RM_xxxx> [--from <hex>] [--to <hex>]
                                                 a room's script opcodes with their file offsets
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

    public string Positional(int index, string what) =>
        index < _positional.Count ? _positional[index] : throw new ArgumentException($"Missing {what}.");
}
