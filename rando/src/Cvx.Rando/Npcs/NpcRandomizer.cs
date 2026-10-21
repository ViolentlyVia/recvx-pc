using Cvx.Formats;
using Cvx.Formats.Rdx;
using Cvx.Rando.Data;
using Cvx.Rando.Enemies;
using Cvx.Rando.Items;

namespace Cvx.Rando.Npcs;

/// <summary>A voice line given to another speaker: VOICE{Disc}.AFS entry Entry gets the ADX of VOICE{FromDisc}.AFS entry FromEntry.</summary>
public sealed record VoiceSwap(int Disc, int Entry, string Speaker, int FromDisc, int FromEntry);

/// <summary>What the NPC shuffle did.</summary>
/// <param name="Looks">Character -> the character whose look and voice they take.</param>
/// <param name="KeptRooms">Rooms whose NPCs could not be changed (their data is not understood or would not fit).</param>
public sealed record NpcResult(IReadOnlyDictionary<string, string> Looks, IReadOnlyList<RoomId> ChangedRooms,
    IReadOnlyList<RoomId> KeptRooms, IReadOnlyList<VoiceSwap> Voices);

/// <summary>
/// NPCs take each other's look and voice (one new look per character for the whole seed, as in
/// BioRand). The look: in every room, their entries keep their type, position and the room's
/// scripts, and get the other character's model and textures (the motions are not in the rooms,
/// and the human cast shares one skeleton). The voice: each line they speak becomes a line of
/// the other character of about the same length, from the game's own voice archives.
/// </summary>
public sealed class NpcRandomizer(RandoData data, RoomArchive archive, Rng rng)
{
    public NpcResult Apply(ItemPatcher rooms)
    {
        var cast = data.Npcs.Characters;
        var looks = Derange(cast.Select(c => c.Name).ToList());
        var byName = cast.ToDictionary(c => c.Name);

        // the donors: each character's model page and textures
        var donors = new Dictionary<string, (ReadOnlyMemory<byte> Model, ReadOnlyMemory<byte>[] Textures)>();
        foreach (var c in cast)
        {
            var (room, index) = ParseLook(c.Look);
            var layout = RdxEnemyLayout.Read(archive.ReadRoom(room));
            var e = layout.Enemies[index];
            donors[c.Name] = (e.Model, e.Textures.Select(t => layout.TextureGroups[t]).ToArray());
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
                var (model, textures) = donors[looks[typeOf[e.Type]]];
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

        return new NpcResult(looks, changed, kept, Voices(looks, byName));
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
        foreach (var line in lines)
        {
            var who = byName.Values.FirstOrDefault(c => c.Voice == line.Speaker);
            if (who == null || looks[who.Name] == who.Name)
                continue;
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
                var replacements = mine.ToDictionary(s => s.Entry, s => Source(s.FromDisc).ReadEntry(s.FromEntry));
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
