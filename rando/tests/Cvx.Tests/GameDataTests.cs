using Cvx.Formats;
using Cvx.Formats.Rdx;

namespace Cvx.Tests;

/// <summary>Round trips against the real game files.</summary>
public class GameDataTests
{
    [GameDataFact]
    public void ArchiveRewriteIsIdentical()
    {
        var data = TestData.Open();
        using var afs = data.OpenRoomArchive();
        using var copy = new MemoryStream();
        AfsWriter.Write(afs, new Dictionary<int, byte[]>(), copy);

        using var original = File.OpenRead(data.RoomArchivePath);
        Assert.Equal(original.Length, copy.Length);
        copy.Position = 0;
        var a = new byte[1 << 20];
        var b = new byte[1 << 20];
        int n;
        while ((n = original.ReadAtLeast(a, a.Length, throwOnEndOfStream: false)) > 0)
        {
            copy.ReadExactly(b, 0, n);
            Assert.True(a.AsSpan(0, n).SequenceEqual(b.AsSpan(0, n)));
        }
    }

    [GameDataFact]
    public void EveryRoomParsesAndRebuildsIdentically()
    {
        using var rooms = RoomArchive.Open(TestData.Open());
        var failures = new List<string>();
        Parallel.ForEach(RoomTable.All, id =>
        {
            var expanded = rooms.ReadExpanded(id);
            var file = new RdxFile(expanded);
            var rebuilt = file.ToBuilder().ToFile();
            if (file.Header.Version < RdxFile.MinimumVersion || !rebuilt.Data.Span.SequenceEqual(expanded))
            {
                lock (failures)
                    failures.Add(id.Name);
            }
        });
        Assert.Empty(failures);
    }

    [GameDataTheory]
    [InlineData("RM_0000")]
    [InlineData("RM_0100")]
    [InlineData("RM_7100")] // the room that grows the most when recompressed
    [InlineData("RM_9350")] // the largest compressed room
    public void RecompressedRoomExpandsWithTheGamesDecoder(string name)
    {
        using var rooms = RoomArchive.Open(TestData.Open());
        var expanded = rooms.ReadExpanded(RoomId.ParseName(name));
        var compressed = Prs.Compress(expanded);
        Assert.Equal(expanded, Expand.Decompress(compressed));
    }

    [GameDataFact]
    public void ItemIdsComeFromTheIdField()
    {
        // RM_0100 (the courtyard outside the prison) has two Green Herbs and Handgun Bullets.
        using var rooms = RoomArchive.Open(TestData.Open());
        var file = rooms.ReadRoom(RoomId.ParseName("RM_0100"));
        Assert.Equal(new[] { 21, 21, 12 }, file.Items.ToArray().Select(x => (int)x.ItemId));
    }

    [GameDataFact]
    public void PatchingARecordChangesOnlyThatRecord()
    {
        using var rooms = RoomArchive.Open(TestData.Open());
        var file = rooms.ReadRoom(RoomId.ParseName("RM_0100"));
        var item = file.Items[2];
        item.ItemId = 21;
        var patched = file.WithRecord(RdxTable.Items, 2, item);

        Assert.Equal(21, patched.Items[2].ItemId);
        var differences = Enumerable.Range(0, file.Length).Count(i => file.Data.Span[i] != patched.Data.Span[i]);
        Assert.Equal(1, differences); // 12 -> 21 is a one-byte change
        Assert.True(patched.ToBuilder().ToFile().Data.Span.SequenceEqual(patched.Data.Span));
    }

    [GameDataFact]
    public void DoorTriggersPointAtRealRooms()
    {
        using var rooms = RoomArchive.Open(TestData.Open());
        var targets = 0;
        foreach (var id in RoomTable.All)
        {
            foreach (var aot in rooms.ReadRoom(id).Aots)
            {
                if (aot.Kind != RdxAot.KindDoor)
                    continue;
                targets++;
                var anyVariant = Enumerable.Range(0, 3).Any(v => RoomTable.Contains(new RoomId(aot.Stage, aot.Room, v)));
                Assert.True(anyVariant, $"{id}: door to stage {aot.Stage} room {aot.Room}");
            }
        }
        Assert.True(targets > 100, $"only {targets} doors found");
    }
}

public sealed class GameDataTheoryAttribute : TheoryAttribute
{
    public GameDataTheoryAttribute()
    {
        if (TestData.Directory == null)
            Skip = "Game data not found (set CVX_DATA or build the port's build_pc\\data).";
    }
}
