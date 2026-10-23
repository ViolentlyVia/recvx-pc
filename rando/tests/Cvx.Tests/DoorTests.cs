using Cvx.Formats;
using Cvx.Formats.Script;
using Cvx.Rando;
using Cvx.Rando.Data;
using Cvx.Rando.Doors;
using Cvx.Rando.Items;

namespace Cvx.Tests;

public class DoorTests
{
    private static readonly RandoData Data = RandoData.Load();

    private static RandoSettings Doors(int seed, Character who = Character.Claire, int segments = 4, int size = 8) =>
        new() { Seed = seed, RandomDoors = true, DoorCharacter = who, DoorSegments = segments, DoorSegmentSize = size };

    private static Generator.DoorLayout Make(int seed, Character who = Character.Claire, int segments = 4, int size = 8) =>
        Generator.MakeDoors(Doors(seed, who, segments, size), Data);

    /// <summary>The first layout (seeds from 1) with all of <paramref name="rooms"/> in one segment.</summary>
    private static Generator.DoorLayout MakeWith(params string[] rooms) =>
        Enumerable.Range(1, 200).Select(seed => Make(seed)).First(l => l.Plan.Segments.Any(x => rooms.All(x.Rooms.Contains)));

    [Theory]
    [InlineData(Character.Claire, 4, 8)]
    [InlineData(Character.Claire, 1, 1)]
    [InlineData(Character.Claire, 4, 1)]
    [InlineData(Character.Claire, 2, 4)]
    [InlineData(Character.Chris, 3, 8)]
    [InlineData(Character.Chris, 1, 2)]
    [InlineData(Character.Chris, 3, 1)]
    public void HundredSeedsAreAllCompletable(Character who, int segments, int size)
    {
        var failures = new List<string>();
        for (var seed = 1; seed <= 100; seed++)
        {
            try
            {
                var layout = Make(seed, who, segments, size);
                var check = Playthrough.Check(layout.Graph, layout.Items);
                if (!check.Completed)
                    failures.Add($"seed {seed}: {check.Message}");
            }
            catch (ItemPlacementException ex)
            {
                failures.Add($"seed {seed}: {ex.Message}");
            }
        }
        Assert.Empty(failures);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void TheStartRoomLeadsToASaveRoom(int seed)
    {
        var plan = Make(seed).Plan;
        Assert.Equal(DoorRandomizer.StartRoom, plan.StartRoom);
        var saveRooms = Generator.SaveRoomKeys(Data);
        var exits = plan.Links.Values.Where(l => l.Door.Room == DoorRandomizer.StartRoom).ToList();
        Assert.Equal(2, exits.Count);
        foreach (var link in exits)
        {
            if (link.Door.OriginalTarget == DoorRandomizer.CellRoom)
                Assert.Equal(DoorRandomizer.StartRoom, link.TargetRoom);   // the cell is skipped
            else
                Assert.Contains(link.TargetRoom, saveRooms);
        }
    }

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    public void DoorsAreTwoWay(int seed)
    {
        var plan = Make(seed).Plan;
        foreach (var link in plan.Links.Values.Where(l => l.Partner != null))
        {
            var back = plan.Links[link.Partner!];
            Assert.Equal(link.Door.Room, back.TargetRoom);
            Assert.Equal(link.Door.Arrival, back.Arrival);
            Assert.Same(link.Door, back.Partner);
        }
    }

    [Theory]
    [InlineData(20, Character.Claire, 4, 8)]
    [InlineData(21, Character.Claire, 2, 3)]
    [InlineData(22, Character.Chris, 3, 8)]
    [InlineData(23, Character.Chris, 1, 1)]
    public void EveryRoomOfTheRunIsReachableAndNoOther(int seed, Character who, int segments, int size)
    {
        // Ignoring keys: every room the run uses is reached from the start (one-way doors
        // included), and every door of those rooms leads into the run.
        var layout = Make(seed, who, segments, size);
        var reached = Reachable(layout.Graph, layout.Plan.StartRoom);
        Assert.Equal(layout.Plan.Rooms.Order(StringComparer.Ordinal), reached.Order(StringComparer.Ordinal));
        var randomizer = new DoorRandomizer(Data, Generator.SaveRoomKeys(Data), Doors(seed, who, segments, size));
        foreach (var stub in randomizer.AllStubs().Where(s => layout.Plan.Rooms.Contains(s.Room)))
        {
            var to = layout.Plan.Find(stub.Room, stub.Index)?.TargetRoom ?? stub.OriginalTarget;
            Assert.True(layout.Plan.Rooms.Contains(to), $"{stub} leads out of the run");
        }
    }

    [Theory]
    [InlineData(Character.Claire, 4, 4)]
    [InlineData(Character.Claire, 2, 2)]
    [InlineData(Character.Chris, 3, 3)]
    [InlineData(Character.Chris, 4, 3)]   // Chris starts after the switch to him: one bridge fewer
    [InlineData(Character.Claire, 1, 1)]
    public void SegmentsEndInOneWayDoors(Character who, int segments, int expected)
    {
        foreach (var seed in Enumerable.Range(1, 10))
        {
            var plan = Make(seed, who, segments).Plan;
            Assert.Equal(expected, plan.Segments.Count);
            Assert.Equal(plan.StartRoom, plan.Segments[0].Entry);
            Assert.Equal(plan.EndRoom, plan.Segments[^1].Exit);
            for (var i = 0; i + 1 < plan.Segments.Count; i++)
            {
                // the exit's one-way story door leads to the next segment's entry
                var bridge = Data.Map.Rooms[plan.Segments[i].Exit].Doors!.Single(d => d.IsBridgeEdge && d.NoReturn);
                Assert.Equal(bridge.Target, plan.Segments[i + 1].Entry);
            }
            Assert.Equal(plan.Rooms.Count, plan.Segments.Sum(x => x.Rooms.Count));   // no room in two segments
        }
    }

    [Fact]
    public void SmallerSegmentsUseFewerRooms()
    {
        double Average(int size) => Enumerable.Range(1, 10).Average(seed => Make(seed, size: size).Plan.Rooms.Count);
        var sizes = new[] { 1, 4, 8 }.Select(Average).ToList();
        Assert.True(sizes[0] < sizes[1] && sizes[1] < sizes[2], string.Join(" ", sizes));
    }

    [Fact]
    public void OneCharacterPlaysTheWholeRun()
    {
        var chris = Make(1, Character.Chris, 3);
        Assert.Equal(DoorRandomizer.ChrisStartRoom, chris.Plan.StartRoom);
        Assert.Equal(Character.Chris, chris.Graph.OnlyCharacter);
        Assert.DoesNotContain(DoorRandomizer.SwitchRoom, chris.Plan.Rooms);
        Assert.Empty(chris.Items.GivenKeys);
        var claire = Make(1);
        Assert.Equal(DoorRandomizer.StartRoom, claire.Plan.StartRoom);
        Assert.Equal(Character.Claire, claire.Graph.OnlyCharacter);

        // every player_change_set the run could meet is removed
        var nops = RoomTweaks.For(new RandoSettings { RandomDoors = true }, Data).Where(t => t.Nop != null).ToList();
        Assert.Contains(nops, t => t.Room == "70A0" && t.Nop!.Value.Contains(0x1F3140));
        Assert.Contains(nops, t => t.Room == "A1C0" && t.Nop!.Value.Contains(0x1BBAE2));
        Assert.Contains(nops, t => t.Room == "A200" && t.Nop!.Value.Contains(0x18753C));
    }

    [Fact]
    public void RandoIniStartsTheRightCharacter()
    {
        string Ini(Character who)
        {
            var layout = Make(1, who, 2);
            return Generator.RandoIni(new GenerationResult
            {
                Settings = Doors(1, who, 2),
                ChangedRooms = new Dictionary<RoomId, Cvx.Formats.Rdx.RdxFile>(),
                Doors = layout.Plan,
                Spoiler = "",
            });
        }
        Assert.Contains("start=0,1,0,0,0", Ini(Character.Claire));
        Assert.Contains("start=7,24,0,0,1", Ini(Character.Chris));   // RM_7240, where Chris arrives
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeptKeyDoorsNeedTheirKeysFromBothSides(bool doors)
    {
        // Found in play (seed 968047): RM_0160 leads into a fenced corner of RM_0080 behind the
        // Eagle Plate door. The data only gives the plate on RM_0080's side.
        var graph = doors ? MakeWith("1100", "1080").Graph : ItemGraph.Build(Data, new RandoSettings { Seed = 1 });
        var eagle = graph.Rooms["1100"].Edges.Where(e => e.To.Key == "1080" && !e.Redirected).ToList();
        Assert.NotEmpty(eagle);
        Assert.All(eagle, e => Assert.Contains(56, e.Requires));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PalacePassageOpensFromTheOfficeOnly(bool doors)
    {
        // Found in play (seed 394125): RM_2000 -> RM_1100 -> RM_1090's corridor, where the
        // bookcase is a wall; it opens from the office with the Gold Lugers.
        var graph = doors ? MakeWith("2090", "20A0").Graph : ItemGraph.Build(Data, new RandoSettings { Seed = 1 });
        var office = graph.Rooms["2090"];
        Assert.All(graph.Rooms["20A0"].Edges.Where(e => e.To == office), e => Assert.True(e.Locked));
        Assert.All(office.Edges.Where(e => e.To.Key == "20A0"), e => Assert.Contains(33, e.Requires));
    }

    [Fact]
    public void ThePlaneDoorKeepsItsPair()
    {
        // Found in play (seed 79634): a random door into RM_4011 through the plane door's pair
        // lands on the gangway, which only the bridge (set from the hangar side) connects.
        var layout = Make(1);   // four segments: every bridge, the plane's too
        var plane = layout.Graph.Rooms["5011"].Edges.Where(e => e.To.Key == "6000").ToList();
        Assert.NotEmpty(plane);
        Assert.All(plane, e => Assert.False(e.Redirected));
        Assert.DoesNotContain(layout.Plan.Links.Values, l => l.TargetRoom == "5011" && l.Arrival == 5);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheAtticLadderGoesUpOnly(bool doors)
    {
        // Found in play (seed 79634): coming down RM_2040's attic ladder (or through a random
        // door paired with it) leaves the player on the bed canopy until the King Ant / Music
        // Box Plate puzzle on the floor lowers the ladder.
        var graph = doors ? MakeWith("3040", "3060").Graph : ItemGraph.Build(Data, new RandoSettings { Seed = 1 });
        Assert.Contains(graph.Rooms["3040"].Edges, e => e.To.Key == "3060" && !e.Redirected && !e.Locked);
        Assert.All(graph.Rooms["3060"].Edges.Where(e => e.To.Key == "3040"), e => Assert.True(e.Locked));
    }

    [Fact]
    public void OnlyThePlaneDoorLeadsIntoThePlane()
    {
        // With random doors RM_5000 (the plane) starts the flight to Antarctica when it loads
        // (its story flags), a one-way trip; a random door into it would skip the rest of
        // Rockfort. Seen with seed 7 in the door sweep.
        foreach (var seed in Enumerable.Range(1, 20))
        {
            var layout = Make(seed);
            Assert.DoesNotContain(layout.Plan.Links.Values, l => l.TargetRoom == "6000");
            Assert.DoesNotContain(layout.Graph.Rooms.Values.SelectMany(r => r.Edges), e => e.To.Key == "6000" && e.Redirected);
            Assert.Contains(layout.Graph.Rooms["5011"].Edges, e => e.To.Key == "6000");
        }
    }

    [Fact]
    public void RandomDoorsOpenTheDoorFromRm1100()
    {
        // Found in play (seed 79634): the door mode nop removes RM_1100's cutscene that sets map
        // flag 0x46, without which the room never sets up its door to RM_2000.
        var tweak = Assert.Single(RoomTweaks.For(new RandoSettings { RandomDoors = true }, Data), t => t.Room == "20A0" && t.Flag != null);
        Assert.Equal(new byte[] { 0x05, 0x08, 0x46, 0x00, 0x00, 0x00 }, tweak.FlagOpcode());
        Assert.DoesNotContain(RoomTweaks.For(new RandoSettings(), Data), t => t.Room == "20A0" && t.Flag != null);
    }

    [Fact]
    public void TheLiftKeepsItsDoors()
    {
        // Found in play (seed 306305): RM_1140 and RM_4050 are one lift; area 3 rides it and
        // area 0's exit depends on the level, not on the room file, so its doors stay as they are.
        // (The run never uses the lift: doors into it lead back into their own rooms.)
        foreach (var seed in Enumerable.Range(1, 20))
        {
            var plan = Make(seed).Plan;
            Assert.DoesNotContain("5050", plan.Rooms);
            Assert.DoesNotContain("20E0", plan.Rooms);
            Assert.DoesNotContain(plan.Links.Values, l => l.Door.Room is "5050" or "20E0" || l.TargetRoom is "5050" or "20E0");
            Assert.All(plan.Links.Values.Where(l => l.Door.OriginalTarget is "5050" or "20E0"), l => Assert.Equal(l.Door.Room, l.TargetRoom));
        }
    }

    [Fact]
    public void RandomDoorsSwitchOffTakenPickups()
    {
        // Found in play (seed 79634): RM_3210's pickup came back on every visit. Its item
        // opcodes only hide the model (last byte 1) and leave switching the area off to the
        // script that the door mode nop removes.
        var doors = RoomTweaks.For(new RandoSettings { RandomDoors = true }, Data).Where(t => t.Room == "4150" && t.Patch != null).ToList();
        Assert.Contains(doors, t => t.Patch == (0x145DF8 + 5, (byte)0));
        Assert.Contains(doors, t => t.Patch == (0x145E6E + 5, (byte)0));
        Assert.Contains(RoomTweaks.For(new RandoSettings { RandomDoors = true }, Data), t => t.Room == "4150" && t.Nop != null);
        Assert.DoesNotContain(RoomTweaks.For(new RandoSettings(), Data), t => t.Room == "4150");
    }

    [Fact]
    public void RandomDoorsKeepScriptDoors()
    {
        // Found in play (seed 79634) and by the door sweep: door mode nops that removed RM_1120's
        // stair climb and RM_0110's door setup (area 11, to RM_0120).
        var nops = RoomTweaks.For(new RandoSettings { RandomDoors = true }, Data).Where(t => t.Nop != null).ToList();
        Assert.DoesNotContain(nops, t => t.Room == "20C0" && t.Nop!.Value.Contains(0x1580B4));
        Assert.DoesNotContain(nops, t => t.Room == "10B0" && t.Nop!.Value.Contains(0x208FD6));
        Assert.Contains(nops, t => t.Room == "10B0" && t.Nop!.Value.Contains(0x208FD4));
    }

    [Fact]
    public void StoryDoorsStayClosedWithRandomDoors()
    {
        // Found in play (seed 389349): on a fresh game RM_2050's door to RM_2040 plays a scene
        // that sends the player to RM_2030 (through a random door), and RM_2040's door back
        // does nothing. Doors like these are left out with random doors (doorRando: false).
        var layout = MakeWith("3050", "3040");
        Assert.DoesNotContain(layout.Graph.Rooms["3050"].Edges, e => e.To.Key == "3040");
        Assert.DoesNotContain(layout.Graph.Rooms["3040"].Edges, e => e.To.Key == "3050");
        Assert.DoesNotContain(layout.Plan.Links.Keys, s => s.Room == "3050" && s.OriginalTarget == "3040");
        var items = ItemGraph.Build(Data, new RandoSettings { Seed = 1 });
        Assert.Contains(items.Rooms["3050"].Edges, e => e.To.Key == "3040" && !e.Locked);
    }

    [Fact]
    public void SameSeedSameDoors()
    {
        string Text(int seed) => DoorTable.Write(Make(seed).Plan, Data);
        Assert.Equal(Text(40), Text(40));
        Assert.NotEqual(Text(40), Text(41));
    }

    [Fact]
    public void DoorTableLinesMatchTheEngineFormat()
    {
        var lines = DoorTable.Write(Make(50).Plan, Data).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith('#')).ToList();
        Assert.NotEmpty(lines);
        foreach (var line in lines)
        {
            var v = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
            Assert.InRange(v.Length, 10, 11);
            Assert.True(RoomTable.Contains(new RoomId(v[0], v[1], v[2])), line);
            Assert.True(RoomTable.Contains(new RoomId(v[6], v[7], v[8])), line);
            if (v.Length == 11)
            {
                // a locked door: one the player opens (an area), leading back into its own room
                Assert.Equal(1, v[10]);
                Assert.NotEqual(255, v[3]);
                Assert.Equal((v[0], v[1]), (v[6], v[7]));
            }
        }
    }

    [Theory]
    [InlineData(117323, 1, 1)]
    [InlineData(5, 4, 8)]
    [InlineData(9, 2, 2)]
    public void NoTwoDoorsOfARoomLeadIntoEachOther(int seed, int segments, int size)
    {
        // seed 117323: RM_7120's doors to RM_7000 and RM_7030 were paired with each other
        var layout = Generator.MakeDoors(Doors(seed, Character.Claire, segments, size), Data);
        Assert.DoesNotContain(layout.Plan.Links.Values, l => l.Partner is { } p && p.Room == l.Door.Room && p != l.Door);
    }

    [Fact]
    public void RM7120sDoorToRM7230IsTheScriptsArea4()
    {
        // the script calls it when trigger area 4 is used; recvx-pc looks script doors up by that
        // area, so with the data's old id 3 it was never redirected (seed 117323 went on into
        // RM_7230, a room of no run, and its scene stuck)
        var door = Data.Map.Rooms["80C0"].Doors!.Single(d => d.Target == "8170");
        Assert.Equal(4, door.Id);
        var layout = Generator.MakeDoors(Doors(117323, Character.Claire, 1, 1) with { ItemMode = ItemMode.Custom, HiddenKeyItems = true }, Data);
        var table = DoorTable.Write(layout.Plan, Data);
        Assert.Contains(table.Split('\n'), l => l.StartsWith("7 12 0 4 7 23 "));
    }

    [Fact]
    public void DoorsThatLeadNowhereAreLocked()
    {
        // seed 365484 (1 segment, size 1/8): RM_3160's two doors to RM_3180 lead nowhere; they
        // were doors back into the same room, now the game shows them locked
        var layout = Generator.MakeDoors(Doors(365484, Character.Claire, 1, 1) with { ItemMode = ItemMode.Custom, HiddenKeyItems = true }, Data);
        var deadEnds = layout.Plan.Links.Values.Where(l => l.TargetRoom == l.Door.Room && l.Partner == null).ToList();
        Assert.NotEmpty(deadEnds);
        Assert.All(deadEnds.Where(l => l.Door.Area != 255 && l.Door.Raw.Offsets is not { Length: > 0 } && l.Door.OriginalTarget != l.Door.Room),
            l => Assert.True(l.Locked, l.Door.ToString()));
        // script doors (offsets) cannot show a lock: they lead back into their room
        Assert.All(layout.Plan.Links.Values.Where(l => l.Door.Raw.Offsets is { Length: > 0 }), l => Assert.False(l.Locked));
        Assert.All(layout.Plan.Links.Values.Where(l => l.Partner != null || l.TargetRoom != l.Door.Room), l => Assert.False(l.Locked));
        Assert.All(layout.Plan.Links.Values.Where(l => l.Door.Area == 255), l => Assert.False(l.Locked));
    }

    [GameDataFact]
    public void TypewriterListMatchesTheRooms()
    {
        using var rooms = RoomArchive.Open(TestData.Open());
        var found = SaveRooms.FindAll(rooms).Where(x => x.Value.Typewriter).Select(x => x.Key.BioRandId).Order(StringComparer.Ordinal);
        Assert.Equal(SaveRooms.Typewriters.Order(StringComparer.Ordinal), found);
    }

    [GameDataFact]
    public void DoorSeedRoomsAreValid()
    {
        // Every tweak lands on an opcode (the patcher checks), rooms with added start opcodes
        // rebuild cleanly, and the flag opcodes are at the start of procedure 0.
        using var rooms = RoomArchive.Open(TestData.Open());
        var result = Generator.Generate(new RandoSettings { Seed = 3, RandomDoors = true }, Data, rooms);
        foreach (var t in RoomTweaks.Fixed.Where(t => t.Flag != null && t.Mode == TweakMode.DoorsOnly))
        {
            var file = result.ChangedRooms[RoomId.ParseBioRand(t.Room)];
            Assert.True(file.ToBuilder().ToFile().Data.Span.SequenceEqual(file.Data.Span));
            var start = file.Script[0].Span;
            var flags = RoomTweaks.Fixed.Where(x => x.Room == t.Room && x.Flag != null).SelectMany(x => x.FlagOpcode()).ToArray();
            Assert.True(start[..flags.Length].SequenceEqual(flags), t.Room);
            Assert.NotEmpty(CvScriptReader.ReadOpcodes(file));
        }
        Assert.NotNull(result.DoorTable);
        Assert.Contains("start=0,1,0,0,0", Generator.RandoIni(result));
    }

    private static HashSet<string> Reachable(ItemGraph graph, string start)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<RoomNode>([graph.Rooms[start]]);
        while (stack.Count != 0)
        {
            var r = stack.Pop();
            if (!seen.Add(r.Key))
                continue;
            foreach (var e in r.Edges.Where(e => !e.Locked))
                stack.Push(e.To);
        }
        return seen;
    }

    [GameDataFact]
    public void NoDoorLeadsOutOfTheRun()
    {
        // The owner's seed 428946 (1 segment, size 1): RM_2060's script door to RM_2050, which
        // the door mode's data does not list, kept its target and led into the rest of the game.
        // Every door a player opens in the run's rooms that leads out of the run has an entry.
        using var rooms = RoomArchive.Open(TestData.Open());
        var settings = Doors(428946, Character.Claire, 1, 1) with
        {
            ItemMode = ItemMode.Custom, IncludeChris = false, HiddenKeyItems = true, RandomInventory = true, StartSidePack = true,
        };
        var owner = Generator.MakeDoors(settings, Data, null, StoryFlags.Rule(rooms, Data, settings));
        Assert.Contains("3060", owner.Plan.Rooms);
        Assert.DoesNotContain("3050", owner.Plan.Rooms);
        var entries = DoorTable.Entries(owner.Plan, Data, rooms);
        Assert.Contains(entries, e => e is { Stage: 2, Room: 6, Area: 9, OldStage: 2, OldRoom: 5, Locked: true });

        foreach (var seed in Enumerable.Range(1, 10))
        {
            var layout = Make(seed, Character.Claire, 1 + seed % 4, 1 + seed % 8);
            var run = layout.Plan.Rooms.Select(RoomId.ParseBioRand).ToHashSet();
            var table = DoorTable.Entries(layout.Plan, Data, rooms);
            foreach (var id in RoomTable.All.Where(id => run.Any(r => r.Stage == id.Stage && r.Room == id.Room)))
            {
                var aots = rooms.ReadRoom(id).Aots.ToArray();
                for (var area = 0; area < aots.Length; area++)
                {
                    var a = aots[area];
                    if (a.Kind != Cvx.Formats.Rdx.RdxAot.KindDoor || (a.Stage == id.Stage && a.Room == id.Room) ||
                        run.Any(r => r.Stage == a.Stage && r.Room == a.Room) || !RoomTable.All.Any(r => r.Stage == a.Stage && r.Room == a.Room))
                        continue;
                    Assert.True(table.Any(e => e.Stage == id.Stage && e.Room == id.Room && e.Case == id.Variant &&
                            e.OldStage == a.Stage && e.OldRoom == a.Room && (e.Area == area || e.Area == 255)),
                        $"seed {seed}: {id} area {area} leads to stage {a.Stage} room {a.Room}, out of the run");
                }
            }
        }
    }

    [Fact]
    public void RunsGetEveryCopyOfAKey()
    {
        // The owner's seed 152118 (1 segment, size 1): RM_9300's door needs the Gold Dragonfly,
        // made from the wingless one and four Wing Objects, but the run placed one Wing Object:
        // the four pickups were in rooms it did not use. A run places as many as the game has.
        var wing = Data.Items.Items.Single(i => i.Name == "Wing Object").Id;
        foreach (var seed in Enumerable.Range(1, 12))
        {
            var layout = Make(seed, Character.Claire, 1 + seed % 4, 1 + seed % 8);
            foreach (var type in layout.Items.KeyOrder.Select(k => k.Type).Distinct())
            {
                var held = layout.Items.ByGlobalId.Values.Count(a => a.Type == type);
                Assert.True(held >= Math.Max(1, layout.Graph.GameCopies.GetValueOrDefault(type)),
                    $"seed {seed}: {Data.Items.NameOf(type)} in {held} pickups, the game has {layout.Graph.GameCopies.GetValueOrDefault(type)}");
            }
            if (layout.Items.KeyOrder.Any(k => k.Type == wing))
                Assert.Equal(4, layout.Items.ByGlobalId.Values.Count(a => a.Type == wing));
        }
    }
}

