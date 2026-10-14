using Cvx.Formats;
using Cvx.Formats.Script;
using Cvx.Rando.Data;

namespace Cvx.Tests;

public class RandoDataTests
{
    private static readonly RandoData Data = RandoData.Load();

    [Fact]
    public void RoomDataLoads()
    {
        Assert.Equal(165, Data.Map.Rooms.Count);
        Assert.Equal(2, Data.Map.BeginEndRooms.Length);
        foreach (var key in Data.Map.Rooms.Keys)
            Assert.True(RoomId.TryParseBioRand(key, out var id) && RoomTable.Contains(id), key);
    }

    [Fact]
    public void ItemCatalogLoads()
    {
        var items = Data.Items.Items;
        Assert.Equal(154, items.Count);
        Assert.Equal(items.Count, items.Select(x => x.Id).Distinct().Count());
        Assert.Equal("Handgun Bullets", Data.Items.NameOf(12));
        Assert.True(Data.Items.Get(55)!.IsKey);          // Lighter
        Assert.Equal([12], Data.Items.Get(9)!.Ammo);     // Handgun -> Handgun Bullets
    }

    [Fact]
    public void EveryReferenceResolves()
    {
        foreach (var (key, room) in Data.Map.Rooms)
        {
            foreach (var door in room.Doors ?? [])
            {
                Assert.True(RoomId.TryParseBioRand(door.Target!, out var t) && RoomTable.Contains(t), $"{key} door -> {door.Target}");
                foreach (var r in door.Requires ?? [])
                    Assert.NotNull(Data.Items.Get(r));
            }
            foreach (var item in room.Items ?? [])
            {
                Assert.NotNull(item.Type);
                Assert.NotNull(Data.Items.Get(item.Type!.Value));
            }
        }
        foreach (var pos in Data.EnemyPositions)
            Assert.True(RoomTable.Contains(pos.RoomId), pos.Room);
    }

    [Theory]
    [InlineData("1625480", 1625480, 1625480)]
    [InlineData("0x18C56A", 0x18C56A, 0x18C56A)]
    [InlineData("0x18C56A-0x18C57A", 0x18C56A, 0x18C57A)]
    public void ParsesOffsets(string text, int start, int end) =>
        Assert.Equal(new OffsetSpec(start, end), OffsetSpec.Parse(text));
}

public class ScriptAndValidationTests
{
    [GameDataFact]
    public void EveryScriptWalksWithKnownOpcodeSizes()
    {
        using var rooms = RoomArchive.Open(TestData.Open());
        var problems = new List<string>();
        Parallel.ForEach(RoomTable.All, id =>
        {
            var file = rooms.ReadRoom(id);
            var opcodes = CvScriptReader.ReadOpcodes(file);
            var bad = opcodes.Count(o => CvOpcodes.GetSize(o.Bytes.Span) != o.Length);
            if (bad != 0)
            {
                lock (problems)
                    problems.Add($"{id}: {bad} opcode(s) with an unknown size");
            }
        });
        Assert.Empty(problems);
    }

    [GameDataFact]
    public void RoomDataMatchesTheGameExceptKnownDoorIssues()
    {
        using var rooms = RoomArchive.Open(TestData.Open());
        var report = new DataValidator(RandoData.Load(), rooms).Validate();

        // Doors still to be checked in the game (data/recv/CHANGES.md, "Open").
        var known = new HashSet<string> { "80F0", "8180", "A0F0", "A1A0", "A1B0" };
        var unexpected = report.Errors.Where(e => !known.Contains(e.Room)).Select(e => e.ToString()).ToList();
        Assert.Empty(unexpected);
        Assert.Equal(7, report.Errors.Count());
        Assert.Equal(400 - 1 + 2, report.ItemsChecked);   // one bogus BioRand item removed, two Shotguns added
    }
}
