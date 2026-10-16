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

    private static Generator.DoorLayout Make(int seed, bool chris = true) =>
        Generator.MakeDoors(new RandoSettings { Seed = seed, RandomDoors = true, IncludeChris = chris }, Data);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HundredSeedsAreAllCompletable(bool chris)
    {
        var failures = new List<string>();
        for (var seed = 1; seed <= 100; seed++)
        {
            try
            {
                var layout = Make(seed, chris);
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
    [InlineData(20, true)]
    [InlineData(21, true)]
    [InlineData(22, false)]
    public void EveryRoomOfTheGameIsStillReachable(int seed, bool chris)
    {
        // Ignoring keys: every room the original layout reaches from the start room (one-way
        // doors included) is reached with random doors too.
        var settings = new RandoSettings { Seed = seed, RandomDoors = true, IncludeChris = chris };
        var layout = Generator.MakeDoors(settings, Data);
        var before = Reachable(ItemGraph.Build(Data, settings), DoorRandomizer.StartRoom);
        before.Remove(DoorRandomizer.CellRoom);   // skipped
        var after = Reachable(layout.Graph, DoorRandomizer.StartRoom);
        Assert.Empty(before.Except(after).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeptKeyDoorsNeedTheirKeysFromBothSides(bool doors)
    {
        // Found in play (seed 968047): RM_0160 leads into a fenced corner of RM_0080 behind the
        // Eagle Plate door. The data only gives the plate on RM_0080's side.
        var settings = new RandoSettings { Seed = 1, RandomDoors = doors };
        var graph = doors ? Generator.MakeDoors(settings, Data).Graph : ItemGraph.Build(Data, settings);
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
        var settings = new RandoSettings { Seed = 1, RandomDoors = doors };
        var graph = doors ? Generator.MakeDoors(settings, Data).Graph : ItemGraph.Build(Data, settings);
        var office = graph.Rooms["2090"];
        Assert.All(graph.Rooms["20A0"].Edges.Where(e => e.To == office), e => Assert.True(e.Locked));
        Assert.All(office.Edges.Where(e => e.To.Key == "20A0"), e => Assert.Contains(33, e.Requires));
    }

    [Fact]
    public void ThePlaneDoorKeepsItsPair()
    {
        // Found in play (seed 79634): a random door into RM_4011 through the plane door's pair
        // lands on the gangway, which only the bridge (set from the hangar side) connects.
        var layout = Generator.MakeDoors(new RandoSettings { Seed = 1, RandomDoors = true }, Data);
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
        var settings = new RandoSettings { Seed = 1, RandomDoors = doors };
        var graph = doors ? Generator.MakeDoors(settings, Data).Graph : ItemGraph.Build(Data, settings);
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
            var layout = Generator.MakeDoors(new RandoSettings { Seed = seed, RandomDoors = true }, Data);
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
        var layout = Generator.MakeDoors(new RandoSettings { Seed = 1, RandomDoors = true }, Data);
        Assert.DoesNotContain(layout.Graph.Rooms["3050"].Edges, e => e.To.Key == "3040");
        Assert.DoesNotContain(layout.Graph.Rooms["3040"].Edges, e => e.To.Key == "3050");
        Assert.DoesNotContain(layout.Plan.Links.Keys, s => s.Room == "3050" && s.OriginalTarget == "3040");
        var items = ItemGraph.Build(Data, new RandoSettings { Seed = 1 });
        Assert.Contains(items.Rooms["3050"].Edges, e => e.To.Key == "3040" && !e.Locked);
    }

    [Fact]
    public void ClaireOnlyKeepsChrisDoors()
    {
        var plan = Make(30, chris: false).Plan;
        Assert.DoesNotContain(plan.Links.Values, l => RoomId.ParseBioRand(l.Door.Room).Stage == 9);
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
            Assert.Equal(10, v.Length);
            Assert.True(RoomTable.Contains(new RoomId(v[0], v[1], v[2])), line);
            Assert.True(RoomTable.Contains(new RoomId(v[6], v[7], v[8])), line);
        }
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
        Assert.Contains("start=0,1,0,0", Generator.RandoIni(result));
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
}
