using Cvx.Formats;
using Cvx.Rando;
using Cvx.Rando.Data;
using Cvx.Rando.Doors;

namespace Cvx.Tests;

public class SeedCheckTests
{
    private static readonly RandoData Data = RandoData.Load();

    [GameDataFact]
    public void AWrittenSeedChecksOutAndABrokenFolderDoesNot()
    {
        var game = TestData.Open();
        using var rooms = RoomArchive.Open(game);
        var settings = new RandoSettings { Seed = 161803, RandomDoors = true, DoorSegments = 2, DoorSegmentSize = 4 };
        var result = Generator.Generate(settings, Data, rooms);
        var dir = Path.Combine(Path.GetTempPath(), "cvx-seedcheck-" + Guid.NewGuid().ToString("N"));
        try
        {
            Generator.WriteMod(result, rooms, dir);
            Assert.Empty(SeedCheck.Check(result, Data, rooms, dir));

            // the game's own rooms: the pickups are not where the logic put them
            File.Copy(game.RoomArchivePath, Path.Combine(dir, GameData.RoomArchiveName), overwrite: true);
            Assert.Contains(SeedCheck.Check(result, Data, rooms, dir), p => p.Contains("item slot"));

            // a door of the plan missing from the table
            var table = Path.Combine(dir, DoorTable.FileName);
            var lines = File.ReadAllLines(table).ToList();
            lines.RemoveAt(lines.FindIndex(l => !l.StartsWith('#')));
            File.WriteAllLines(table, lines);
            Assert.Contains(SeedCheck.Check(result, Data, rooms, dir), p => p.StartsWith(DoorTable.FileName));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
