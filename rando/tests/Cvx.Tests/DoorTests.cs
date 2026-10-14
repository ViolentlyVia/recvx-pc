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
