using System.Text;
using Cvx.Formats;
using Cvx.Rando.Data;
using Cvx.Rando.Music;
using Cvx.Rando.Npcs;

namespace Cvx.Rando;

/// <summary>The spoiler log: where everything went.</summary>
public static class Spoiler
{
    public static string Write(GenerationResult result, RandoData data)
    {
        var s = result.Settings;
        var names = data.Items;
        var sb = new StringBuilder();
        sb.AppendLine("CVX Randomizer spoiler log");
        sb.AppendLine("==========================");
        sb.AppendLine($"Seed: {s.Seed}");
        sb.AppendLine($"Items: {(s.RandomItems ? s.ItemMode.ToString().ToLowerInvariant() : "off")}" +
                      $"{(s.RandomItems && !s.IncludeChris && !s.RandomDoors ? " (Claire's half only)" : "")}");
        sb.AppendLine($"Doors: {(s.RandomDoors ? $"random, {s.DoorCharacter} only, {s.DoorSegments} segments, size {s.DoorSegmentSize}/8" : "off")}");
        if (s.StartSidePack)
            sb.AppendLine("Side pack: from the start");
        sb.AppendLine($"Enemies: {(s.RandomEnemies ? $"random, difficulty {s.EnemyDifficulty}" + EnemyMix(s, data) : "off")}");
        sb.AppendLine($"Music: {(s.RandomMusic ? "shuffled" + MusicFrom(s, result) : "off")}");
        sb.AppendLine($"NPCs: {(result.Npcs is { } n ? string.Join(", ", n.Looks.Select(x => $"{x.Key} as {NpcResult.LookName(x.Value)}")) : "off")}");
        if (s.RandomTitleCall)
            sb.AppendLine($"Title call: {(result.TitleCall is { } t ? $"BioRand's {t.Name}" : "the game's (no BioRand folder with title calls)")}");
        sb.AppendLine();

        if (result.Doors is { } doors)
        {
            var start = RoomId.ParseBioRand(doors.StartRoom);
            sb.AppendLine("Doors");
            sb.AppendLine("-----");
            sb.AppendLine($"  {doors.Character} plays the whole run; a new game starts in {RoomLabel(start)}.");
            sb.AppendLine($"  {doors.Rooms.Count} rooms in {doors.Segments.Count} segments:");
            foreach (var (segment, i) in doors.Segments.Select((x, i) => (x, i)))
            {
                var exit = segment.Exit == doors.EndRoom ? "the end" : "a one-way door";
                sb.AppendLine($"    {i + 1}. {RoomLabel(RoomId.ParseBioRand(segment.Entry))} to {RoomLabel(RoomId.ParseBioRand(segment.Exit))} ({exit}), {segment.Rooms.Count} rooms");
            }
            foreach (var room in doors.Links.Values.GroupBy(l => l.Door.Room).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                sb.AppendLine($"  {RoomLabel(RoomId.ParseBioRand(room.Key))}");
                foreach (var link in room.OrderBy(l => l.Door.Index))
                {
                    var to = link.TargetRoom == link.Door.Room
                        ? (link.Locked ? "locked (leads nowhere)" : "back into this room")
                        : $"{RoomLabel(RoomId.ParseBioRand(link.TargetRoom))} entrance {link.Arrival}";
                    sb.AppendLine($"    door to {link.Door.OriginalTarget,-5} -> {to}");
                }
            }
            sb.AppendLine();
        }

        if (result.Items is { } items)
        {
            sb.AppendLine("Key items, in the order the logic expects them");
            sb.AppendLine("----------------------------------------------");
            foreach (var a in items.KeyOrder)
            {
                var half = result.Doors?.Character.ToString() ?? (a.Location.Room.IsChris ? "Chris" : "Claire");
                sb.AppendLine($"  {names.NameOf(a.Type),-28} {RoomLabel(a.Location.Room.Id)}  ({half})");
            }
            if (items.GivenKeys.Count != 0)
            {
                sb.AppendLine();
                sb.AppendLine("  Handed over by the game (not randomized):");
                foreach (var (half, type) in items.GivenKeys)
                    sb.AppendLine($"    {names.NameOf(type)} ({half})");
            }
            sb.AppendLine();

            sb.AppendLine("All pickups by room");
            sb.AppendLine("-------------------");
            foreach (var room in items.ByGlobalId.Values.GroupBy(a => a.Location.Room).OrderBy(g => g.Key.Key, StringComparer.Ordinal))
            {
                sb.AppendLine($"  {RoomLabel(room.Key.Id)}");
                foreach (var a in room.OrderBy(a => a.Location.GlobalId))
                {
                    var was = a.Location.OriginalType == a.Type ? "(unchanged)" : $"(was {names.NameOf(a.Location.OriginalType)})";
                    sb.AppendLine($"    {names.NameOf(a.Type),-28} {was}{(a.IsKey ? "  [key]" : "")}");
                }
            }
            sb.AppendLine();
        }

        if (result.StoryFlags.Count != 0)
        {
            // rooms that check another room's pickup for a key item now check where the item is
            sb.AppendLine("Story checks (item flags other rooms read)");
            sb.AppendLine("------------");
            foreach (var group in result.StoryFlags.Where(c => c.To != null && c.Why.StartsWith("follows ", StringComparison.Ordinal) && c.Why != "follows its spot")
                         .GroupBy(c => (c.Why, c.To)).OrderBy(g => g.Key.Why, StringComparer.Ordinal))
                sb.AppendLine($"  {group.Key.Why["follows ".Length..]} (flag {group.Key.To}): {string.Join(", ", group.Select(c => c.Room.Name).Distinct())}");
            foreach (var c in result.StoryFlags.Where(c => c.Why.EndsWith("(unchanged)", StringComparison.Ordinal)).DistinctBy(c => (c.Room, c.Why)))
                sb.AppendLine($"  {c.Room.Name}: {c.Why}");
            foreach (var c in result.StoryFlags.Where(c => c.To == null))
                sb.AppendLine($"  {c.Room.Name}: {c.Why}");
            sb.AppendLine();
        }

        if (result.Enemies.Count != 0 || result.EnemiesCleared.Count != 0)
        {
            string Name(int type) => data.Enemies.Types.FirstOrDefault(k => k.Type == type)?.Name ?? $"enemy {type}";
            sb.AppendLine("Enemies");
            sb.AppendLine("-------");
            foreach (var c in result.Enemies)
            {
                var was = c.OldTypes.Count == 0 ? "none" : string.Join(", ", c.OldTypes.GroupBy(t => t).Select(g => $"{g.Count()} {Name(g.Key)}"));
                var now = c.Moved ? $"{c.Count} {c.NewKind.Name}" : c.NewKind.Name;
                var placed = c.Moved
                    ? $"  (new places{(c.Added != 0 ? $", {c.Added} more" : "")}{(c.Hidden != 0 ? $", {c.Hidden} gone" : "")})"
                    : "";
                sb.AppendLine($"  {RoomLabel(c.Room)}  {was} -> {now}{placed}{(c.SoundFrom == null ? "  (sounds unchanged)" : "")}");
            }
            foreach (var room in result.EnemiesCleared)
                sb.AppendLine($"  {RoomLabel(room)}  no enemies");
            if (result.SharedEnemyFlags != 0)
                sb.AppendLine($"  ({result.SharedEnemyFlags} new enemies share a death flag with another room's: killing one also clears the other)");
            sb.AppendLine();
        }

        if (result.Music.Count != 0)
        {
            sb.AppendLine("Music (track numbers as in dump-music)");
            sb.AppendLine("-----");
            foreach (var group in result.Music.GroupBy(m => m.Category))
            {
                sb.AppendLine($"  {group.Key}: {string.Join(", ", group.Where(m => m.Import == null).Select(m => m.From == m.To ? $"{m.From}" : $"{m.From}->{m.To}"))}");
                foreach (var m in group.Where(m => m.Import != null))
                    sb.AppendLine($"    {m.From,3} -> {MusicLibrary.DisplayName(m.Import!.Game)}: {m.Import.Tag}/{m.Import.Name}");
            }
            sb.AppendLine();
        }

        if (result.Npcs is { } npcs)
        {
            sb.AppendLine("NPCs (look and voice)");
            sb.AppendLine("---------------------");
            foreach (var (who, look) in npcs.Looks)
            {
                sb.AppendLine(NpcResult.IsBioRand(look)
                    ? $"  {who,-8} looks and sounds like {NpcResult.LookName(look)} (BioRand's model and voice clips)"
                    : $"  {who,-8} looks and sounds like {look}");
            }
            if (npcs.MissingCharacters.Count != 0)
                sb.AppendLine($"  Not in the BioRand folder, left out: {string.Join(", ", npcs.MissingCharacters.Select(NpcResult.LookName))}");
            sb.AppendLine($"  {npcs.ChangedRooms.Count} rooms changed, {npcs.Voices.Count} voice lines given to the new voices.");
            sb.AppendLine($"  Rooms: {string.Join(" ", npcs.ChangedRooms.Select(r => r.Name))}");
            if (npcs.KeptRooms.Count != 0)
                sb.AppendLine($"  Unchanged (room data not understood or too big): {string.Join(" ", npcs.KeptRooms.Select(r => r.Name))}");
            sb.AppendLine();
        }

        foreach (var inventory in result.StartInventories)
        {
            var who = inventory.Character == "chris" ? "Chris" : "Claire";
            sb.AppendLine($"{who}'s starting items");
            sb.AppendLine(new string('-', who.Length + 17));
            foreach (var i in inventory.Items)
                sb.AppendLine($"  {names.NameOf(i.Type),-28} {i.Count}");
            sb.AppendLine();
        }

        if (result.PickupAmounts.Count != 0)
        {
            sb.AppendLine("Pickup amounts");
            sb.AppendLine("--------------");
            foreach (var (type, amount) in result.PickupAmounts.OrderBy(x => x.Key))
                sb.AppendLine($"  {names.NameOf(type),-28} {amount}");
            sb.AppendLine();
        }

        sb.AppendLine($"{result.ChangedRooms.Count} room files changed, {result.Patches.Count} patches.");
        return sb.ToString();
    }

    private static string RoomLabel(RoomId id) => $"{id.Name} ({id.BioRandId})";

    private static string MusicFrom(RandoSettings s, GenerationResult result)
    {
        if (s.MusicGames.All(g => g == MusicLibrary.OwnGame))
            return "";
        var games = s.MusicGames.Select(MusicLibrary.DisplayName).ToList();
        var text = $", with music from {string.Join(", ", games)}";
        if (result.MissingMusicGames.Count != 0)
            text += $" (not in the BioRand folder, left out: {string.Join(", ", result.MissingMusicGames.Select(MusicLibrary.DisplayName))})";
        return text;
    }

    private static string EnemyMix(RandoSettings s, RandoData data) =>
        s.EnemyRatios.Count == 0 ? ""
            : ", mix " + string.Join(", ", data.Enemies.Types.Select(k => $"{k.Name} {k.RatioIn(s)}/{EnemyKind.MaxRatio}"));
}

