using System.IO.Compression;
using System.Numerics;
using Cvx.Formats;
using Cvx.Formats.Models;
using Cvx.Formats.Rdx;
using Cvx.Rando;
using Cvx.Rando.Data;
using Cvx.Rando.Items;
using Cvx.Rando.Npcs;
using Cvx.Rando.Setup;

namespace Cvx.Tests;

/// <summary>A test that needs BioRand's classic datapack; skipped when no "biorand" folder is found.</summary>
public sealed class BioRandFactAttribute : FactAttribute
{
    public BioRandFactAttribute()
    {
        if (BioRandData.ClassicPack == null)
            Skip = "BioRand's datapacks/classic.zip not found (a \"biorand\" folder next to or above the tests, or build_pc\\biorand).";
        else if (TestData.Directory == null)
            Skip = "Game data not found (set CVX_DATA or build the port's build_pc\\data).";
    }
}

internal static class BioRandData
{
    public static string? ClassicPack { get; } = Find();

    /// <summary>The BioRand folder that holds <see cref="ClassicPack"/>.</summary>
    public static string? Directory => ClassicPack == null ? null : Path.GetDirectoryName(Path.GetDirectoryName(ClassicPack));

    private static string? Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            // a "biorand" folder, or the one next to the port's recvx.exe (build_pc\biorand)
            foreach (var folder in new[] { "biorand", Path.Combine("build_pc", "biorand") })
            {
                var path = Path.Combine(dir.FullName, folder, "datapacks", "classic.zip");
                if (File.Exists(path))
                    return path;
            }
        }
        return null;
    }

    public static byte[] Read(string entry)
    {
        using var zip = ZipFile.OpenRead(ClassicPack!);
        using var stream = (zip.GetEntry(entry) ?? throw new FileNotFoundException(entry)).Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}

public class CharacterTests
{
    private const string Leon = "data/re2/pld0/leon.re2/PL00.PLD";

    [BioRandFact]
    public void VoiceClipsBecomeTheGamesVoiceLines()
    {
        var library = CharacterLibrary.Open(BioRandData.Directory)!;
        var leon = library.VoiceClips("leon.re2");
        Assert.True(leon.Count >= 100, $"{leon.Count} clips");
        Assert.All(leon, c => Assert.True(c.Seconds > 0, c.Name));
        Assert.Contains(leon, c => c.IsPlain);
        Assert.Contains(leon, c => !c.HasNoKind);   // _radio
        Assert.Equal(library.VoiceClips("leon.re2").Select(c => c.Name), library.VoiceClips("leon.re2$variant").Select(c => c.Name));

        // as a line of the game: one-shot mono ADX, as long as the clip, as loud as the line it replaces
        using var afs = AfsArchive.Open(Path.Combine(TestData.Directory!, "VOICE1.AFS"));
        var original = afs.ReadEntry(23);   // one of Steve's
        var clip = leon.First(c => c.IsPlain);
        var (pcm, rate) = Adx.Decode(NpcRandomizer.ClipAsAdx(clip, original));
        Assert.Single(pcm);
        Assert.InRange(pcm[0].Length / (double)rate, clip.Seconds - 0.1, clip.Seconds + 0.1);
        // (or as loud as it goes without clipping: a clip with peaks near full scale stays quieter)
        var want = Cvx.Rando.Music.MusicLibrary.Level(Adx.Decode(original).Channels).Rms;
        var (rms, peak) = Cvx.Rando.Music.MusicLibrary.Level(pcm);
        Assert.True(rms <= want * 1.3 && (rms >= want * 0.7 || peak >= 30000), $"rms {rms:0}, peak {peak}, the line's rms {want:0}");
    }

    [BioRandFact]
    public void ReadsAnRe2Character()
    {
        var pld = Pld.Read(BioRandData.Read(Leon));
        Assert.Equal(15, pld.Armature.Count);
        Assert.Equal(19, pld.Mesh.Parts.Count);
        Assert.Equal([1, 8, 9, 12], pld.Armature.Children[0]);
        // Y points down: the head is above the feet
        var rest = pld.Armature.RestPositions();
        Assert.True(rest[8].Y < rest[4].Y - 2000);
        Assert.Equal((512, 256, 8), (pld.Texture.Width, pld.Texture.Height, pld.Texture.BitsPerPixel));
        Assert.Equal(128, pld.Texture.PageWidth);
        Assert.All(pld.Mesh.Parts.Take(15).SelectMany(p => p.Polygons), p => Assert.InRange(p.Page, 0, 1));
    }

    [BioRandFact]
    public void LeonBecomesAnNpcModel()
    {
        using var rooms = RoomArchive.Open(TestData.Open());
        var room = RoomId.ParseName("RM_7220");
        var file = rooms.ReadRoom(room);
        var layout = RdxEnemyLayout.Read(file);
        var chris = layout.Enemies[1];
        Assert.Equal(92, chris.Type);
        var skeleton = ModelPage.Read(chris.Model.Span).Models.First();
        var template = TextureEntry.Read(layout.TextureGroups[chris.Textures[0]].Span).Pictures.First(p => p.Width == 256);

        var converted = CharacterConverter.Convert(Pld.Read(BioRandData.Read(Leon)), skeleton, template);
        var page = converted.Page.ToBytes();
        var model = Assert.Single(ModelPage.Read(page).Models);
        Assert.Equal(page, ModelPage.Read(page).ToBytes());

        // the skeleton is the target's tree; the 15 parts ride on their objects, nothing else has a model
        Assert.Equal(skeleton.Objects.Select(o => (o.Child, o.Sibling)), model.Objects.Select(o => (o.Child, o.Sibling)));
        var withModels = model.Objects.Select((o, i) => (o, i)).Where(x => x.o.Model != null).Select(x => x.i).ToList();
        // (the hands ride the forearms: the game's motions do not pose the wrists, 14 and 18)
        Assert.Equal([1, 3, 5, 12, 13, 16, 17, 19, 20, 21, 23, 24, 25], withModels);
        Assert.All(withModels, i => Assert.Equal(0u, model.Objects[i].EvalFlags & NjObject.EvalHide));

        // the joints are Leon's (scaled so his neck is as high as the target's): his shoulders as
        // wide as his own, his ankles at the target's height
        var bind = model.BindPositions();
        var pld = Pld.Read(BioRandData.Read(Leon));
        var rest = pld.Armature.RestPositions();
        var target = skeleton.BindPositions();
        var scale = (target[4].Y - (target[21].Y + target[25].Y) / 2) / ((rest[4].Y + rest[7].Y) / 2 - rest[8].Y);
        Assert.Equal(Vector3.Distance(rest[9], rest[12]) * scale, Vector3.Distance(bind[12], bind[16]), 0.01f);
        Assert.Equal(Vector3.Distance(rest[10], rest[9]) * scale, Vector3.Distance(bind[13], bind[12]), 0.01f);
        Assert.Equal((target[21].Y + target[25].Y) / 2, (bind[21].Y + bind[25].Y) / 2, 0.01f);
        // (about: the leg bones keep their length but take the target's slightly different slant)
        Assert.Equal(target[4].Y, bind[4].Y, 0.15f);
        Assert.All(model.Objects, o => Assert.Equal(Vector3.One, o.Scale));

        // he stands on the ground at about the target's height, facing -Z (his nose in front of his head)
        var points = withModels.SelectMany(i => model.Objects[i].Model!.Vertices![0].Ps2Vertices().Select(v => bind[i] + v.Position)).ToList();
        Assert.InRange(points.Min(p => p.Y), -0.5f, 0.5f);
        Assert.InRange(points.Max(p => p.Y), 16f, 20f);
        var head = model.Objects[5].Model!.Vertices![0].Ps2Vertices().Select(v => bind[5] + v.Position).ToList();
        Assert.True(head.Min(p => p.Z) < bind[5].Z - 0.5f);

        // textures: one per page and CLUT, with global indexes of their own
        var pictures = converted.Textures.Pictures.ToList();
        Assert.Equal(2, pictures.Count);
        Assert.Equal(model.Textures!.Count, pictures.Count);
        Assert.All(pictures, p => Assert.True(p.GlobalIndex >= CharacterConverter.FirstGlobalIndex));
        Assert.Equal(converted.Textures.ToBytes(), TextureEntry.Read(converted.Textures.ToBytes()).ToBytes());

        // the room takes it and still fits the game's room memory
        var donor = new RdxEnemyDonor(room, 1, chris.Type, chris.ModelVersion, chris.Entry.Effect, (byte)(chris.Entry.Index >> 8), page, chris.Motion, [converted.Textures.ToBytes()]);
        var result = RdxEnemyRewriter.Replace(file, new Dictionary<int, RdxEnemyDonor> { [1] = donor });
        var check = RdxEnemyLayout.Read(result);
        Assert.True(check.IsConsistent);
        // (the room's page list pads each page to 64 bytes)
        Assert.Equal(page, check.Enemies[1].Model.ToArray().Take(page.Length));
        Assert.True(Expand.FitsRoomPool(Prs.Compress(result.Data.Span), out _, out _));
    }

    [BioRandFact]
    public void ConvertedStripsFaceOutward()
    {
        // the game's winding (checked on its own models): a strip's even triangles (a, b, c) face
        // along cross(b - a, c - a), odd ones the other way, all flipped for a negative length
        using var rooms = RoomArchive.Open(TestData.Open());
        var layout = RdxEnemyLayout.Read(rooms.ReadRoom(RoomId.ParseName("RM_0030")));
        var steve = ModelPage.Read(layout.Enemies[2].Model.Span).Models.First();
        Assert.True(Outward(steve) > 0.99);

        var template = TextureEntry.Read(layout.TextureGroups[layout.Enemies[2].Textures[0]].Span).Pictures.First(p => p.Width == 256);
        var leon = CharacterConverter.Convert(Pld.Read(BioRandData.Read(Leon)), steve, template).Page.Models.First();
        Assert.True(Outward(leon) > 0.95);
    }

    /// <summary>Every texture of the game's rooms has a global index below the converted characters'.</summary>
    [GameDataFact]
    public void ConvertedTexturesHaveGlobalIndexesOfTheirOwn()
    {
        using var rooms = RoomArchive.Open(TestData.Open());
        var max = 0u;
        foreach (var id in RoomTable.All)
        {
            foreach (var group in rooms.ReadRoom(id).Textures.Groups)
            {
                TextureEntry entry;
                try
                {
                    entry = TextureEntry.Read(group.Span);
                }
                catch (InvalidDataException)
                {
                    continue;
                }
                foreach (var r in entry.Records.Where(r => r.Code == TextureRecord.Tim2Code && r.Data.Length >= 0x100))
                    max = Math.Max(max, new Tim2Picture(r.Data).GlobalIndex & 0x3FFFFFF);
            }
        }
        Assert.InRange(max, 1u, CharacterConverter.FirstGlobalIndex - 1);
    }

    [Fact]
    public void CharactersHaveNames()
    {
        Assert.Equal("Leon (RE2)", CharacterLibrary.DisplayName("leon.re2"));
        Assert.Equal("Chris (RE1, battle)", CharacterLibrary.DisplayName("chris.re1$battle"));
        Assert.Equal("Steve (Code: Veronica)", CharacterLibrary.DisplayName("steve.recv"));
        Assert.Equal("Leon (RE2)", NpcResult.LookName("leon.re2"));
        Assert.Equal("Wesker", NpcResult.LookName("Wesker"));
    }

    [Fact]
    public void TheLibraryFindsLooseCharacters()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cvx-characters-" + Guid.NewGuid().ToString("N"), "biorand");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "data", "re2", "pld0", "leon.re2"));
            Directory.CreateDirectory(Path.Combine(dir, "data", "re2", "pld1", "ada.re2"));
            Directory.CreateDirectory(Path.Combine(dir, "data", "re1", "pld0", "barry.re1"));
            File.WriteAllBytes(Path.Combine(dir, "data", "re2", "pld0", "leon.re2", "PL00.PLD"), [1]);
            File.WriteAllBytes(Path.Combine(dir, "data", "re2", "pld1", "ada.re2", "PL01.PLD"), [2]);
            File.WriteAllBytes(Path.Combine(dir, "data", "re2", "pld1", "ada.re2", "PL01W00.PLW"), [3]);
            File.WriteAllBytes(Path.Combine(dir, "data", "re1", "pld0", "barry.re1", "PL00.PLD"), [4]);

            var library = CharacterLibrary.Find(null, Path.GetDirectoryName(dir));
            Assert.NotNull(library);
            Assert.Equal(["ada.re2", "leon.re2"], library.Characters.Select(c => c.Id));
            Assert.Equal([2], CharacterLibrary.ReadPld(library.Get("ADA.re2")!));
            Assert.Null(CharacterLibrary.Open(Path.Combine(dir, "data")));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);
        }
    }

    [Fact]
    public void NoCharactersPickedLeavesShareCodesAlone()
    {
        var settings = new RandoSettings { Seed = 5, RandomNpcs = true };
        Assert.Empty(settings.NpcCharacters);
        Assert.DoesNotContain(nameof(RandoSettings.NpcCharacters), Json(ShareCode.Encode(settings)));
        var picked = settings with { NpcCharacters = ["ada.re2", "leon.re2"] };
        Assert.True(ShareCode.TryDecode(ShareCode.Encode(picked), out var back, out _));
        Assert.Equal(picked.NpcCharacters, back!.NpcCharacters);
    }

    [BioRandFact]
    public void ClaireAndChrisTakeBioRandLooksAndVoices()
    {
        using var rooms = RoomArchive.Open(TestData.Open());
        var data = RandoData.Load();
        var library = CharacterLibrary.Open(BioRandData.Directory)!;

        // chosen, or random and never both the same
        var chosen = Generator.PlayerLooks(new RandoSettings { Seed = 1, ClaireLook = "Leon.re2", ChrisLook = "ada.re2" }, library);
        Assert.Equal(new Dictionary<string, string> { ["Claire"] = "leon.re2", ["Chris"] = "ada.re2" }, chosen);
        foreach (var seed in Enumerable.Range(1, 20))
        {
            var random = Generator.PlayerLooks(new RandoSettings { Seed = seed }, library);
            Assert.NotEqual(random["Claire"], random["Chris"]);
            Assert.All(random.Values, id => Assert.NotNull(library.Get(id)));
        }
        Assert.Empty(Generator.PlayerLooks(new RandoSettings { Seed = 1 }, null));

        // the players only: the NPCs stay themselves
        var patcher = new ItemPatcher(data, rooms);
        var result = new NpcRandomizer(data, rooms, Rng.For(5, "npcs"), library, ["hunk.re2"], chosen, shuffle: false).Apply(patcher);
        Assert.All(data.Npcs.Characters, c => Assert.Equal(c.Name, result.Looks[c.Name]));
        Assert.Equal("leon.re2", result.Looks["Claire"]);
        Assert.Equal("ada.re2", result.Looks["Chris"]);
        Assert.Empty(result.KeptRooms);

        // their cutscene models (91, 92) become converted pages; nobody else's changes
        var claire = data.Npcs.Players.Single(p => p.Name == "Claire");
        var chris = data.Npcs.Players.Single(p => p.Name == "Chris");
        foreach (var id in result.ChangedRooms)
        {
            var after = RdxEnemyLayout.Read(patcher.ChangedRooms[id]);
            var before = RdxEnemyLayout.Read(rooms.ReadRoom(id));
            Assert.True(after.IsConsistent, $"{id}");
            Assert.Contains(after.Enemies, e => claire.Types.Contains(e.Type) || chris.Types.Contains(e.Type));
            foreach (var (e, was) in after.Enemies.Zip(before.Enemies))
            {
                var player = claire.Types.Contains(e.Type) || chris.Types.Contains(e.Type);
                Assert.Equal(player, !e.Model.Span.SequenceEqual(was.Model.Span));
                if (player)
                    Assert.Equal(CharacterConverter.SkeletonObjects, ModelPage.Read(e.Model.Span).Models.Single().Objects.Count);
            }
        }
        Assert.Contains(RoomId.ParseName("RM_0000"), result.ChangedRooms);
        Assert.Contains(RoomId.ParseName("RM_7220"), result.ChangedRooms);

        // every line the data gives them becomes a clip of their new character; nobody else's changes
        foreach (var (player, look) in new[] { (claire, "leon.re2"), (chris, "ada.re2") })
        {
            var lines = data.Npcs.Voices.Values.Sum(d => d.Values.Count(s => s == player.Voice));
            Assert.True(lines >= 30, $"{player.Name}: {lines} lines");
            var theirs = result.Voices.Where(v => v.Speaker == player.Voice).ToList();
            Assert.Equal(lines, theirs.Count);
            Assert.All(theirs, v => Assert.Equal(look, v.Clip?.Character));
        }
        Assert.All(result.Voices, v => Assert.Contains(v.Speaker, new[] { claire.Voice, chris.Voice }));

        // their gameplay models: Claire's two player files, Chris's one
        Assert.Equal(new[] { 10, 11, 14 }, result.PlayerFiles.Keys.Order());
        // their pain and death sounds: both characters have BioRand's four
        Assert.Equal(new ushort[] { 0xFFF0, 0xFFF1 }, result.PlayerSounds.Keys.Order());
        Assert.All(result.PlayerSounds.Values, clips => Assert.Equal(new[] { 0, 1, 2, 3 }, clips.Keys.Order()));
    }

    [BioRandFact]
    public void PlayerPainSoundsBecomeTheCharactersHurtClips()
    {
        var library = CharacterLibrary.Open(BioRandData.Directory)!;
        using var packs = SoundPacks.Open(TestData.Open(), 1);
        foreach (var (player, look) in new[] { ("Claire", "leon.re2"), ("Chris", "ada.re2") })
        {
            var index = packs.Keys.ToList().IndexOf(PlayerSounds.KeyOf(player));
            var pack = packs.Afs.ReadEntry(index);
            var clips = library.HurtClips(look).ToDictionary(x => x.Key, x => (CharacterLibrary.ReadOgg(x.Value), x.Value.Name));
            Assert.Equal(4, clips.Count);
            var after = PlayerSounds.Replace(pack, clips);

            static (byte[] Hd, byte[] Bd) Split(byte[] p)
            {
                var bank = SoundPacks.Parse(p).Single(b => b.Type == PlayerSounds.BankType && b.BankNo == PlayerSounds.BankNo).Data.ToArray();
                int U(int at) => BitConverter.ToInt32(bank, at);
                return (bank.AsSpan(U(0), U(4)).ToArray(), bank.AsSpan(U(8), U(12)).ToArray());
            }
            var (oldHd, oldBd) = Split(pack);
            var (hd, bd) = Split(after);
            Assert.Equal(oldHd.Length, hd.Length);
            var programs = PlayerSounds.Programs(hd);
            Assert.Equal(new[] { 0, 1, 2, 3 }, programs.Select(p => p.Program));

            // each program plays its clip (program 1, the death, BioRand's 3), mono at the new rate, as long as the clip (or the cap)
            int[] clipOf = [0, 3, 1, 2];
            foreach (var (program, vag) in programs)
            {
                Assert.Equal(PlayerSounds.Rate, BitConverter.ToUInt16(hd, vag + 4));
                var samples = Vag.Decode(bd, BitConverter.ToInt32(hd, vag));
                var (pcm, rate) = PlayerSounds.Decode(clips[clipOf[program]].Item1, clips[clipOf[program]].Name);
                var seconds = Math.Min(pcm.Length / (double)rate, clipOf[program] == 3 ? 3.0 : 1.2);
                Assert.InRange(samples.Length / (double)PlayerSounds.Rate, seconds, seconds + 2 * Vag.SamplesPerBlock / (double)PlayerSounds.Rate);
            }

            // the rest of the pack as it was
            var before = SoundPacks.Parse(pack);
            var banks = SoundPacks.Parse(after);
            Assert.Equal(before.Select(b => (b.Type, b.BankNo)), banks.Select(b => (b.Type, b.BankNo)));
            Assert.All(before.Zip(banks).Where(x => x.First.Type != PlayerSounds.BankType || x.First.BankNo != PlayerSounds.BankNo),
                x => Assert.True(x.First.Data.Span.SequenceEqual(x.Second.Data.Span)));
        }
    }

    [BioRandFact]
    public void PlayerFilesGetTheBodyAndKeepTheRest()
    {
        using var afs = AfsArchive.Open(Path.Combine(TestData.Directory!, PlayerModel.Archive));
        var library = CharacterLibrary.Open(BioRandData.Directory)!;
        var pld = Pld.Read(CharacterLibrary.ReadPld(library.Get("leon.re2")!));
        foreach (var entry in new[] { 10, 11, 14 })
        {
            var file = afs.ReadEntry(entry);
            var before = PlayerModel.Layout.Read(file);
            // the loader reads every texture entry: none is left over
            Assert.Equal(file.Length - 4, before.TailAt);

            var after = PlayerModel.Replace(file, pld, PlayerModel.GlobalIndexOf(entry));
            var layout = PlayerModel.Layout.Read(after);
            Assert.Equal(after.Length - 4, layout.TailAt);

            // the body: the player skeleton's objects, the parts on them, no SKIN block before it
            var bodyBlock = layout.Page.Blocks.FindIndex(b => b.Model != null);
            Assert.Equal(CharacterRig.Player.Objects, layout.Page.Blocks[bodyBlock].Model!.Objects.Count);
            Assert.True(bodyBlock == 0 || layout.Page.Blocks[bodyBlock - 1].Kind != ModelPageBlockKind.Skin);
            Assert.Equal(before.Page.Models.First().Objects.Select(o => o.Child), layout.Page.Models.First().Objects.Select(o => o.Child));
            Assert.All(TextureEntry.Read(layout.Textures[0].Data).Pictures, p => Assert.True(p.GlobalIndex >= PlayerModel.GlobalIndexOf(entry)));

            // the blinks (models 1 and 2's textures) are the body's: no flash of the game's character
            Assert.All(layout.Textures.Skip(1).Take(PlayerModel.BlinkModels), t => Assert.True(t.Data.AsSpan().SequenceEqual(layout.Textures[0].Data)));

            // Claire's ponytail (model 4) is hidden; Chris has none
            var models = layout.Page.Blocks.Where(b => b.Kind == ModelPageBlockKind.Model).ToList();
            if (entry == 11)
                Assert.Equal(PlayerModel.PonytailModel, models.Count);
            else
                Assert.All(models[PlayerModel.PonytailModel].Model!.Objects, o => Assert.True((o.EvalFlags & NjObject.EvalHide) != 0));

            // everything else as it was: the other blocks, the motions, the other texture entries
            var oldBody = before.Page.Blocks.FindIndex(b => b.Model != null);
            var oldSkin = oldBody > 0 && before.Page.Blocks[oldBody - 1].Kind == ModelPageBlockKind.Skin ? oldBody - 1 : -1;
            var oldModels = before.Page.Blocks.Where(b => b.Kind == ModelPageBlockKind.Model).ToList();
            var oldHair = oldModels.Count > PlayerModel.PonytailModel ? before.Page.Blocks.IndexOf(oldModels[PlayerModel.PonytailModel]) : -1;
            var newHair = models.Count > PlayerModel.PonytailModel ? layout.Page.Blocks.IndexOf(models[PlayerModel.PonytailModel]) : -1;
            static string Hex(ModelPageBlock b) => Convert.ToHexString(b.Data);
            Assert.Equal(before.Page.Blocks.Where((_, i) => i != oldBody && i != oldSkin && i != oldHair).Select(Hex),
                         layout.Page.Blocks.Where((_, i) => i != bodyBlock && i != newHair).Select(Hex));
            Assert.True(file.AsSpan(before.MotionsAt, before.TexturesAt - before.MotionsAt).SequenceEqual(after.AsSpan(layout.MotionsAt, layout.TexturesAt - layout.MotionsAt)));
            Assert.Equal(before.Textures.Count, layout.Textures.Count);
            Assert.All(before.Textures.Zip(layout.Textures).Skip(1 + PlayerModel.BlinkModels), x =>
            {
                Assert.True(x.First.Data.AsSpan().SequenceEqual(x.Second.Data));
                Assert.Equal(x.First.Aligned, x.Second.Aligned);
            });
        }
    }

    [Fact]
    public void PlayerLooksLeaveShareCodesAlone()
    {
        var settings = new RandoSettings { Seed = 5 };
        var json = Json(ShareCode.Encode(settings));
        Assert.DoesNotContain(nameof(RandoSettings.RandomPlayers), json);
        Assert.DoesNotContain(nameof(RandoSettings.ClaireLook), json);
        var picked = settings with { RandomPlayers = true, ClaireLook = "leon.re2" };
        Assert.True(ShareCode.TryDecode(ShareCode.Encode(picked), out var back, out _));
        Assert.True(back!.RandomPlayers);
        Assert.Equal("leon.re2", back.ClaireLook);
        Assert.Equal("", back.ChrisLook);
    }

    [BioRandFact]
    public void BioRandCharactersBecomeNpcLooks()
    {
        var data = RandoData.Load();
        using var rooms = RoomArchive.Open(TestData.Open());
        var library = CharacterLibrary.Open(BioRandData.Directory)!;
        var names = data.Npcs.Characters.Select(c => c.Name).ToList();

        // nothing picked: the shuffle is the one it always was
        var plain = new NpcRandomizer(data, rooms, Rng.For(7, "npcs")).Apply(new ItemPatcher(data, rooms));
        var none = new NpcRandomizer(data, rooms, Rng.For(7, "npcs"), library, []).Apply(new ItemPatcher(data, rooms));
        Assert.Equal(plain.Looks, none.Looks);
        Assert.Equal(plain.Voices.Count, none.Voices.Count);

        // the RE2 cast picked (and one that is not there)
        var re2 = library.Characters.Where(c => c.Id.EndsWith(".re2")).Select(c => c.Id).Append("nobody.re9").ToArray();
        var seen = new HashSet<string>();
        foreach (var seed in Enumerable.Range(1, 6))
        {
            var patcher = new ItemPatcher(data, rooms);
            var result = new NpcRandomizer(data, rooms, Rng.For(seed, "npcs"), library, re2).Apply(patcher);
            Assert.Equal(["nobody.re9"], result.MissingCharacters);
            Assert.All(result.Looks, x => Assert.NotEqual(x.Key, x.Value));
            Assert.Equal(names.Count, result.Looks.Values.Distinct().Count());
            Assert.All(result.Looks.Values, l => Assert.True(names.Contains(l) || re2.Contains(l)));
            Assert.Empty(result.KeptRooms);
            seen.UnionWith(result.Looks.Values.Where(NpcResult.IsBioRand));

            // an NPC with a BioRand look speaks that character's clips (every line: they all have clips)
            foreach (var (name, look) in result.Looks.Where(x => NpcResult.IsBioRand(x.Value)))
            {
                var voice = data.Npcs.Characters.Single(c => c.Name == name).Voice;
                var theirs = result.Voices.Where(v => v.Speaker == voice).ToList();
                Assert.NotEmpty(theirs);
                Assert.All(theirs, v => Assert.Equal(look.Split('$')[0], v.Clip?.Character));
                Assert.Equal(data.Npcs.Voices.Values.Sum(d => d.Values.Count(s => s == voice)), theirs.Count);
            }
            // the others' lines are the game's own
            Assert.All(result.Voices.Where(v => v.Clip == null), v => Assert.True(v.FromDisc is 1 or 2));

            // their entries get a page with no SKIN, on the human skeleton, and the rooms still read
            foreach (var id in result.ChangedRooms)
            {
                var after = RdxEnemyLayout.Read(patcher.ChangedRooms[id]);
                Assert.True(after.IsConsistent, $"{id}");
                foreach (var e in after.Enemies)
                {
                    var who = data.Npcs.Characters.FirstOrDefault(c => c.Types.Contains(e.Type));
                    if (who == null || !NpcResult.IsBioRand(result.Looks[who.Name]))
                        continue;
                    var page = ModelPage.Read(e.Model.Span);
                    Assert.DoesNotContain(page.Blocks, b => b.Kind == ModelPageBlockKind.Skin);
                    Assert.Equal(CharacterConverter.SkeletonObjects, page.Models.Single().Objects.Count);
                    Assert.All(e.Textures.SelectMany(t => TextureEntry.Read(after.TextureGroups[t].Span).Pictures),
                        p => Assert.True(p.GlobalIndex >= CharacterConverter.FirstGlobalIndex));
                }
            }
        }
        Assert.True(seen.Count >= 4, string.Join(", ", seen));
    }

    /// <summary>The settings JSON inside a share code.</summary>
    private static string Json(string code)
    {
        var b64 = code[ShareCode.Prefix.Length..].Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
        using var input = new DeflateStream(new MemoryStream(Convert.FromBase64String(b64)), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return System.Text.Encoding.UTF8.GetString(output.ToArray());
    }

    private static double Outward(NjModel model)
    {
        int agree = 0, total = 0;
        foreach (var m in model.Objects.Select(o => o.Model).OfType<NjChunkModel>().Distinct())
        {
            var verts = new Dictionary<int, (Vector3 P, Vector3 N)>();
            foreach (var c in m.Vertices!.Where(c => c.IsPs2))
            {
                var n = c.IndexOffset;
                foreach (var v in c.Ps2Vertices())
                    verts[n++] = v;
            }
            foreach (var strip in m.Polygons!.Where(c => c.Type is >= 64 and <= 66).SelectMany(c => c.Strips()))
            {
                for (var i = 0; i + 2 < strip.Vertices.Count; i++)
                {
                    var (a, b, c) = (verts[strip.Vertices[i].Index], verts[strip.Vertices[i + 1].Index], verts[strip.Vertices[i + 2].Index]);
                    var face = Vector3.Cross(b.P - a.P, c.P - a.P);
                    if ((i % 2 == 1) != strip.Flipped)
                        face = -face;
                    if (Vector3.Dot(face, a.N + b.N + c.N) > 0)
                        agree++;
                    total++;
                }
            }
        }
        return (double)agree / total;
    }
}
