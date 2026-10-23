using Cvx.Formats;

namespace Cvx.Tests;

public class RoomIdTests
{
    [Theory]
    [InlineData("RM_0000", "1000")]
    [InlineData("RM_0100", "10A0")]
    [InlineData("RM_0160", "1100")]
    [InlineData("RM_0021", "1021")]
    [InlineData("RM_9300", "A1E0")]
    [InlineData("RM_9321", "A201")]
    public void BioRandMapping(string name, string bioRand)
    {
        var id = RoomId.ParseName(name);
        Assert.Equal(bioRand, id.BioRandId);
        Assert.Equal(id, RoomId.ParseBioRand(bioRand));
        Assert.True(RoomTable.Contains(id));
    }

    [Theory]
    [InlineData("RM_0100.RDX")]
    [InlineData("rm_0100")]
    [InlineData("0100")]
    public void ParsesNameForms(string text) => Assert.Equal(new RoomId(0, 10, 0), RoomId.ParseName(text));

    [Fact]
    public void TableHas205UniqueRooms()
    {
        Assert.Equal(205, RoomTable.All.Count);
        Assert.Equal(205, RoomTable.All.Distinct().Count());
        Assert.Equal(0, RoomTable.IndexOf(RoomId.ParseName("RM_0000")));
        Assert.Equal(204, RoomTable.IndexOf(RoomId.ParseName("RM_9370")));
    }
}
