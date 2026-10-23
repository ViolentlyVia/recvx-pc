using Cvx.Formats;
using Cvx.Formats.Models;
using Cvx.Formats.Rdx;
using Cvx.Rando.Data;
using Cvx.Rando.Enemies;
using Cvx.Rando.Items;

namespace Cvx.Rando.Npcs;

/// <summary>
/// A voice line given to another speaker: VOICE{Disc}.AFS entry Entry gets the ADX of
/// VOICE{FromDisc}.AFS entry FromEntry, or BioRand's <see cref="Clip"/> (then FromDisc and FromEntry are 0).
/// </summary>
public sealed record VoiceSwap(int Disc, int Entry, string Speaker, int FromDisc, int FromEntry, VoiceClip? Clip = null);

/// <summary>What the NPC shuffle did.</summary>
/// <param name="Looks">Character -> the character whose look and voice they take: another NPC, or one of BioRand's (its id, e.g. leon.re2).</param>
/// <param name="KeptRooms">Rooms whose NPCs could not be changed (their data is not understood or would not fit).</param>
/// <param name="MissingCharacters">BioRand characters the settings ask for that the BioRand folder does not have (or that could not be read).</param>
public sealed record NpcResult(IReadOnlyDictionary<string, string> Looks, IReadOnlyList<RoomId> ChangedRooms,
    IReadOnlyList<RoomId> KeptRooms, IReadOnlyList<VoiceSwap> Voices)
{
    public IReadOnlyList<string> MissingCharacters { get; init; } = [];

    /// <summary>A look as the spoiler and the window say it: "Wesker", "Leon (RE2)".</summary>
    public static string LookName(string look) => IsBioRand(look) ? CharacterLibrary.DisplayName(look) : look;

    /// <summary>BioRand characters' ids have the game after a dot (the game's NPCs' names have none).</summary>
    public static bool IsBioRand(string look) => look.Contains('.');
}

/// <summary>
/// NPCs take each other's look and voice (one new look per character for the whole seed, as in
/// BioRand). The look: in every room, their entries keep their type, position and the room's
/// scripts, and get the other character's model and textures (the motions are not in the rooms,
/// and the human cast shares one skeleton). The voice: each line they speak becomes a line of
/// the other character of about the same length, from the game's own voice archives.
/// With BioRand's characters picked (<paramref name="characters"/>), they are looks too: one is
/// converted onto the skeleton of the NPC who takes it (<see cref="CharacterConverter"/>), and
/// that NPC's lines become that character's clips from BioRand's voice folder, as BioRand does.
/// Claire and Chris (<paramref name="players"/>: name -> BioRand id) take their BioRand look the
/// same way, in their cutscene models, and their lines that the data knows become that
/// character's clips; with <paramref name="shuffle"/> off the NPCs stay themselves.
/// </summary>
public sealed class NpcRandomizer(RandoData data, RoomArchive archive, Rng rng, CharacterLibrary? library = null, IReadOnlyList<string>? characters = null,
    IReadOnlyDictionary<string, string>? players = null, bool shuffle = true)
{
    public NpcResult Apply(ItemPatcher rooms)
    {
        var npcs = data.Npcs.Characters;
        var playerLooks = (players ?? new Dictionary<string, string>()).ToDictionary(x => x.Key, x => x.Value.ToLowerInvariant());
        var cast = npcs.Concat(data.Npcs.Players.Where(p => playerLooks.ContainsKey(p.Name))).ToList();
        var byName = cast.ToDictionary(c => c.Name);

        // BioRand's characters the settings ask for, in the library's order
        Dictionary<string, Pld> Read(IReadOnlySet<string> ids)
        {
            var plds = new Dictionary<string, Pld>();
            foreach (var c in library?.Characters.Where(c => ids.Contains(c.Id)) ?? [])
            {
                try
                {
                    var pld = Pld.Read(CharacterLibrary.ReadPld(c));
                    if (pld.Armature.Count >= 15 && pld.Mesh.Parts.Count >= 15)
                        plds[c.Id] = pld;
                }
                catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException or IndexOutOfRangeException or ArgumentOutOfRangeException)
                {
                    // left out: it is reported missing
                }
            }
            return plds;
        }
        var picked = (shuffle ? characters ?? [] : []).Select(c => c.ToLowerInvariant()).ToHashSet();
        var plds = Read(picked);
        var forPlayers = Read(playerLooks.Values.ToHashSet());
        var missing = picked.Concat(playerLooks.Values).Where(c => !plds.ContainsKey(c) && !forPlayers.ContainsKey(c))
            .Distinct().Order(StringComparer.Ordinal).ToList();

        var names = npcs.Select(c => c.Name).ToList();
        var looks = !shuffle ? names.ToDictionary(n => n, n => n)
            : plds.Count == 0 ? Derange(names) : Assign(names, [.. names, .. plds.Keys]);
        // a player whose character could not be read stays themselves
        foreach (var (name, look) in playerLooks)
            looks[name] = forPlayers.ContainsKey(look) ? look : name;
        foreach (var (id, pld) in forPlayers)
            plds.TryAdd(id, pld);

        // the donors: each character's model page and textures
        var donors = new Dictionary<string, (ReadOnlyMemory<byte> Model, ReadOnlyMemory<byte>[] Textures)>();
        foreach (var c in cast)
        {
            var (room, index) = ParseLook(c.Look);
            var layout = RdxEnemyLayout.Read(archive.ReadRoom(room));
            var e = layout.Enemies[index];
            donors[c.Name] = (e.Model, e.Textures.Select(t => layout.TextureGroups[t]).ToArray());
        }

        // each NPC's new look: another NPC's model, or a BioRand character made for their own
        // skeleton (texture global indexes of their own per NPC: two can share a room)
        var lookOf = new Dictionary<string, (ReadOnlyMemory<byte> Model, ReadOnlyMemory<byte>[] Textures)>();
        foreach (var (c, k) in cast.Select((c, k) => (c, k)))
        {
            if (looks[c.Name] == c.Name)
                continue;
            var look = looks[c.Name];
            if (!NpcResult.IsBioRand(look))
            {
                lookOf[c.Name] = donors[look];
                continue;
            }
            var (model, textures) = donors[c.Name];
            var skeleton = ModelPage.Read(model.Span).Models.First();
            var template = textures.SelectMany(t => TextureEntry.Read(t.Span).Pictures).First(p => p.Width == 256 && p.Height == 256 && p.ImageType == 5);
            var converted = CharacterConverter.Convert(plds[look], skeleton, template, CharacterConverter.FirstGlobalIndex + (uint)k * 16);
            lookOf[c.Name] = (converted.Page.ToBytes(), [converted.Textures.ToBytes()]);
        }

        var typeOf = cast.SelectMany(c => c.Types.Select(t => (t, c.Name))).ToDictionary(x => x.t, x => x.Name);
        var changed = new List<RoomId>();
        var kept = new List<RoomId>();
        foreach (var id in RoomTable.All)
        {
            var original = archive.ReadRoom(id);
            var layout = RdxEnemyLayout.Read(original);
            var targets = layout.Enemies.Where(e => typeOf.ContainsKey(e.Type) && looks[typeOf[e.Type]] != typeOf[e.Type]).ToList();
            if (targets.Count == 0)
                continue;
            if (!layout.IsConsistent || layout.Enemies.Any(e => !e.HasModel))
            {
                kept.Add(id);
                continue;
            }
            var plan = targets.ToDictionary(e => e.Index, e =>
            {
                var (model, textures) = lookOf[typeOf[e.Type]];
                return new RdxEnemyDonor(id, e.Index, e.Type, e.ModelVersion, e.Entry.Effect, (byte)(e.Entry.Index >> 8), model, e.Motion, textures);
            });
            try
            {
                var rebuilt = RdxEnemyRewriter.Replace(rooms.Current(id), plan);
                if (rebuilt.Length > Expand.RoomPool - EnemyRandomizer.MemoryMargin)
                {
                    kept.Add(id);
                    continue;
                }
            }
            catch (Exception ex) when (ex is RdxEnemyRewriter.ConflictException or InvalidDataException)
            {
                kept.Add(id);
                continue;
            }
            rooms.SetRebuild(id, file => RdxEnemyRewriter.Replace(file, plan));
            changed.Add(id);
        }

        return new NpcResult(looks, changed, kept, Voices(looks, byName)) { MissingCharacters = missing };
    }

    /// <summary>
    /// Every character gets another look of the pool, no two the same: the pool in a random
    /// order, each character taking the first look left that is not their own.
    /// </summary>
    private Dictionary<string, string> Assign(List<string> names, List<string> pool)
    {
        var order = rng.Shuffle(pool);
        var result = new Dictionary<string, string>();
        foreach (var name in names)
        {
            var look = order.First(l => l != name && !result.ContainsValue(l));
            result[name] = look;
        }
        return result;
    }

    /// <summary>Every character gets another's look (a random derangement).</summary>
    private Dictionary<string, string> Derange(List<string> names)
    {
        for (var attempt = 0; ; attempt++)
        {
            var order = rng.Shuffle(names);
            if (attempt < 100 && names.Where((n, i) => order[i] == n).Any())
                continue;
            return names.Select((n, i) => (n, order[i])).ToDictionary(x => x.n, x => x.Item2);
        }
    }

    private static (RoomId Room, int Index) ParseLook(string look)
    {
        var parts = look.Split(':');
        return (RoomId.ParseName(parts[0]), int.Parse(parts[1]));
    }

    /// <summary>
    /// Each line of a character becomes a line of the one whose look they took: one of the few
    /// closest in length, the least used first, so the scenes' timing holds.
    /// </summary>
    private List<VoiceSwap> Voices(Dictionary<string, string> looks, Dictionary<string, NpcCharacter> byName)
    {
        if (archive.Data is not { } game)
            return [];
        var lines = new List<(int Disc, int Entry, string Speaker, double Seconds)>();
        foreach (var disc in new[] { 1, 2 })
        {
            if (!data.Npcs.Voices.TryGetValue(disc.ToString(), out var labels))
                continue;
            using var afs = AfsArchive.Open(Path.Combine(game.Directory, $"VOICE{disc}.AFS"));
            foreach (var (entry, speaker) in labels.OrderBy(x => x.Key))
            {
                if (entry < afs.Count && AdxInfo.Read(afs.ReadEntry(entry).AsSpan(0, Math.Min(64, afs.Entries[entry].Size))) is { } info)
                    lines.Add((disc, entry, speaker, info.Seconds));
            }
        }
        var voiceOf = byName.Values.ToDictionary(c => c.Name, c => c.Voice);
        var used = new Dictionary<(int, int), int>();
        var swaps = new List<VoiceSwap>();

        // BioRand's characters: their clips of no special kind (radio, doom...), plain ones
        // (no conditions) when there are enough; a character with few clips repeats them
        var clips = looks.Values.Where(NpcResult.IsBioRand).Distinct().ToDictionary(l => l, l =>
        {
            var all = library?.VoiceClips(l).Where(c => c.HasNoKind && c.Seconds > 0).ToList() ?? [];
            var plain = all.Where(c => c.IsPlain).ToList();
            return plain.Count >= 10 ? plain : all;
        });
        var clipUse = new Dictionary<VoiceClip, int>();

        foreach (var line in lines)
        {
            var who = byName.Values.FirstOrDefault(c => c.Voice == line.Speaker);
            if (who == null || looks[who.Name] == who.Name)
                continue;
            if (NpcResult.IsBioRand(looks[who.Name]))
            {
                // one of the few clips closest in length, the least used first (as below)
                var clip = clips[looks[who.Name]]
                    .OrderBy(c => Math.Abs(c.Seconds - line.Seconds))
                    .Take(4)
                    .OrderBy(c => clipUse.GetValueOrDefault(c))
                    .ThenBy(_ => rng.Next(1000))
                    .FirstOrDefault();
                if (clip == null)
                    continue;   // no clips: the NPC keeps this line
                clipUse[clip] = clipUse.GetValueOrDefault(clip) + 1;
                swaps.Add(new VoiceSwap(line.Disc, line.Entry, line.Speaker, 0, 0, clip));
                continue;
            }
            var newVoice = voiceOf[looks[who.Name]];
            var pick = lines.Where(l => l.Speaker == newVoice)
                .OrderBy(l => Math.Abs(l.Seconds - line.Seconds))
                .Take(4)
                .OrderBy(l => used.GetValueOrDefault((l.Disc, l.Entry)))
                .ThenBy(_ => rng.Next(1000))
                .FirstOrDefault();
            if (pick.Speaker == null)
                continue;
            used[(pick.Disc, pick.Entry)] = used.GetValueOrDefault((pick.Disc, pick.Entry)) + 1;
            swaps.Add(new VoiceSwap(line.Disc, line.Entry, line.Speaker, pick.Disc, pick.Entry));
        }
        return swaps;
    }

    /// <summary>
    /// A BioRand clip as one of the game's voice lines: one-shot mono ADX (the game's are mono,
    /// 48 kHz; the clip keeps its rate, which the ADX header gives), as loud as the line it replaces.
    /// </summary>
    public static byte[] ClipAsAdx(VoiceClip clip, byte[] original)
    {
        var (pcm, rate) = Music.MusicLibrary.DecodeOgg(CharacterLibrary.ReadOgg(clip));
        var mono = new short[pcm[0].Length];
        for (var i = 0; i < mono.Length; i++)
            mono[i] = (short)((pcm[0][i] + pcm[1][i]) / 2);
        var (rms, peak) = Music.MusicLibrary.Level([mono]);
        var target = Music.MusicLibrary.Level(Adx.Decode(original).Channels).Rms;
        var gain = Music.MusicLibrary.MatchGain(rms, peak, target);
        if (Math.Abs(gain - 1) > 0.01)
        {
            for (var i = 0; i < mono.Length; i++)
                mono[i] = (short)Math.Clamp(Math.Round(mono[i] * gain), short.MinValue, short.MaxValue);
        }
        return Adx.Encode([mono], rate, loop: false);
    }

    /// <summary>VOICE1.AFS and VOICE2.AFS with the swapped lines, in the mod folder (or removed when there are none).</summary>
    public static void WriteVoices(GameData game, IReadOnlyList<VoiceSwap> swaps, string directory)
    {
        var sources = new Dictionary<int, AfsArchive>();
        try
        {
            AfsArchive Source(int disc) => sources.TryGetValue(disc, out var a) ? a
                : sources[disc] = AfsArchive.Open(Path.Combine(game.Directory, $"VOICE{disc}.AFS"));
            foreach (var disc in new[] { 1, 2 })
            {
                var path = Path.Combine(directory, $"VOICE{disc}.AFS");
                var mine = swaps.Where(s => s.Disc == disc).ToList();
                if (mine.Count == 0)
                {
                    if (File.Exists(path))
                        File.Delete(path);
                    continue;
                }
                var replacements = mine.ToDictionary(s => s.Entry, s => s.Clip is { } clip
                    ? ClipAsAdx(clip, Source(disc).ReadEntry(s.Entry))
                    : Source(s.FromDisc).ReadEntry(s.FromEntry));
                AfsWriter.Write(Source(disc), replacements, path);
            }
        }
        finally
        {
            foreach (var a in sources.Values)
                a.Dispose();
        }
    }
}
