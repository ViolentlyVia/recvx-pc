using System.Text.Json;
using Cvx.Rando;
using Cvx.Rando.Setup;

namespace Cvx.Tests;

public sealed class SetupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cvx-setup-" + Guid.NewGuid().ToString("N"));

    public SetupTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Ini => Path.Combine(_dir, "recvx.ini");

    [Fact]
    public void PlayAndPlayNormalRestoreTheOwnSaves()
    {
        File.WriteAllText(Ini, "scale=2\r\nsaves=D:\\my saves\r\nkey_up=W\r\n");
        var game = new GameFolder(_dir);

        game.Play(42);
        var ini = game.ReadIni();
        Assert.Equal(@"rando\42", ini.Get("mod"));
        Assert.Equal(@"saves_rando\42", ini.Get("saves"));
        Assert.Equal(@"D:\my saves", ini.Get("saves_normal"));
        Assert.True(Directory.Exists(Path.Combine(_dir, "saves_rando", "42")));

        game.Play(43);   // another seed keeps the first saves_normal
        Assert.Equal(@"D:\my saves", game.ReadIni().Get("saves_normal"));

        game.PlayNormal();
        Assert.Equal("scale=2\r\nsaves=D:\\my saves\r\nkey_up=W\r\n", File.ReadAllText(Ini));
    }

    [Fact]
    public void PlayNormalWithoutOwnSavesRemovesSaves()
    {
        File.WriteAllText(Ini, "scale=2\n");
        var game = new GameFolder(_dir);
        game.Play(7);
        Assert.Equal("*", game.ReadIni().Get("saves_normal"));
        game.PlayNormal();
        Assert.Equal("scale=2\n", File.ReadAllText(Ini));
    }

    [Fact]
    public void IniSetKeepsTheLastLineAndOrder()
    {
        File.WriteAllText(Ini, "a=1\nMOD=x\nb=2\nmod=y\n");
        var ini = IniFile.Load(Ini);
        Assert.Equal("y", ini.Get("mod"));
        ini.Set("mod", "z");
        ini.Save();
        Assert.Equal("a=1\nb=2\nmod=z\n", File.ReadAllText(Ini));
    }

    [Fact]
    public void ActiveSeedComesFromTheModFolder()
    {
        var game = new GameFolder(_dir);
        Assert.Null(game.ActiveSeed);
        Directory.CreateDirectory(game.SeedDirectory(9));
        File.WriteAllText(Path.Combine(game.SeedDirectory(9), "rando.ini"), "version=1\nseed=9\n");
        game.Play(9);
        Assert.Equal("9", game.ActiveSeed);
    }

    [Fact]
    public void ShareCodesRoundTrip()
    {
        var settings = new RandoSettings
        {
            Seed = 123456, ItemMode = ItemMode.Custom, RandomDoors = true, RandomEnemies = true, EnemyDifficulty = 3,
            RandomMusic = true, Weapons = [3, 7], StartWeapon1 = WeaponChoice.Powerful,
        };
        var code = ShareCode.Encode(settings);
        Assert.StartsWith(ShareCode.Prefix, code);
        Assert.True(code.Length < 300, code);
        Assert.True(ShareCode.TryDecode(code, out var back, out _));
        Assert.Equal(JsonSerializer.Serialize(settings), JsonSerializer.Serialize(back));
        Assert.False(ShareCode.TryDecode("CVX1-not-a-code", out _, out _));
        Assert.False(ShareCode.TryDecode("hello", out _, out _));
    }
}
