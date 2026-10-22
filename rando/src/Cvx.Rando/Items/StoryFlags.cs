using Cvx.Formats;
using Cvx.Formats.Script;
using Cvx.Rando.Data;

namespace Cvx.Rando.Items;

/// <summary>One script change <see cref="StoryFlags.Apply"/> made, for the spoiler log and tests.</summary>
/// <param name="To">The flag it reads now; null when the opcode was blanked.</param>
/// <param name="Why">"follows Briefcase", "follows its spot", "keeps its pickup when ...", or "... (unchanged)".</param>
public sealed record StoryFlagChange(RoomId Room, int Offset, string Opcode, int From, int? To, string Why);

/// <summary>
/// Item flags (<c>it_flg</c>, flag group 7) that rooms read as story progress. The game marks a
/// pickup taken with its own flag (<c>ck_item</c>), and other rooms use that flag to mean "the
/// player has the item that lies there": RM_0090's cutscene needs the Briefcase's, the stage 2
/// rooms change version with the King Ant Object's, the stage 3 card reader rooms with the
/// Biohazard Card's, RM_1001 and RM_2040 wait for the three proofs' (see PLAN.md, "Cross-room
/// pickup flags"). Once items move, the spot no longer says anything about the item. So:
/// <list type="bullet">
/// <item>a check or set in another room of a key item spot's flag follows the item: it names the
///   flag of the spot that holds the item now (the logic's map states its needs in items too);</item>
/// <item>checks in the spot's own room follow the spot (they show or hide its pickup);</item>
/// <item>scripts no longer take a randomized pickup away because of something done in another
///   room: Claire's later rooms that hide their copy of a pickup she took earlier (RM_7020,
///   RM_7150), and rooms that hide another room's pickup when they start (RM_4000, RM_4020). The
///   copies are separate spots for the logic, so each keeps its own item.</item>
/// </list>
/// The courtyard switch (RM_0010, RM_0050: flag 4) keeps meaning the Sp Alloy Emblem
/// (<see cref="RoomTweaks.TieCourtyardToEmblem"/>). For a check to follow an item, the item must
/// lie where a pickup flag is set: the placer sees to that (<see cref="Rule"/>).
/// </summary>
public static class StoryFlags
{
    /// <summary>
    /// Pickups the map lists as script gives (no trigger area of their own), with the item flag
    /// their room's pickup sets: RM_2070's area 26 runs the event that hands over the Air Force
    /// Proof (map entry 254), and its <c>ck_item</c> sets flag 18.
    /// </summary>
    private static readonly Dictionary<(string Room, int Id), int> ScriptedPickupFlags = new()
    {
        [("3070", 254)] = 18,
    };

    /// <summary>A pickup location of the map: the global id and its listings (room, map entry).</summary>
    private sealed record Spot(int GlobalId, int OriginalType, List<(string Room, int Id)> Listings, int? Flag);

    /// <summary>
    /// For the item placer: <see cref="Keys"/> (the key items whose pickup flag other rooms read,
    /// and the Sp Alloy Emblem) go to <see cref="Locations"/> (pickups that set a flag when
    /// taken), so the checks can follow them. 64 of the map's 401 pickups set none (the game
    /// hands those over in an event).
    /// </summary>
    public sealed record FlagRule(IReadOnlySet<int> Keys, IReadOnlySet<int> Locations);

    public static FlagRule Rule(RoomArchive archive, RandoData data, RandoSettings settings)
    {
        var uses = PickupFlags.Uses(archive);
        var spots = Spots(uses, data, settings);
        var byFlag = spots.Values.Where(s => s.Flag != null).GroupBy(s => s.Flag!.Value).ToDictionary(g => g.Key, g => g.First());
        var keys = new HashSet<int> { RoomTweaks.SpAlloyEmblem };
        foreach (var use in uses.Where(u => !u.IsPickup))
        {
            if (byFlag.TryGetValue(use.Flag, out var origin) && data.Items.Get(origin.OriginalType) is { IsKey: true, IsDocument: false } &&
                origin.Listings.All(l => !SameRoom(RoomId.ParseBioRand(l.Room), use.Room)))
                keys.Add(origin.OriginalType);
        }
        return new FlagRule(keys, spots.Values.Where(s => s.Flag != null).Select(s => s.GlobalId).ToHashSet());
    }

    private static Dictionary<int, Spot> Spots(IReadOnlyList<PickupFlagUse> uses, RandoData data, RandoSettings settings)
    {
        var spots = new Dictionary<int, Spot>();
        foreach (var (key, room) in data.Map.Rooms)
        {
            foreach (var item in room.Items ?? [])
            {
                if (item.GlobalId is not int g || item.Type is not int type || (item.DoorRando is bool dr && dr != settings.RandomDoors))
                    continue;
                if (!spots.TryGetValue(g, out var spot))
                    spots[g] = spot = new Spot(g, type, [], null);
                spot.Listings.Add((key, item.Id));
                if (spot.Flag == null && FlagOf(uses, key, item.Id) is int f)
                    spots[g] = spot with { Flag = f };
            }
        }
        return spots;
    }

    /// <param name="swap">The courtyard's pickup swap: the emblem spot's old flag (it and 4 swapped places).</param>
    public static List<StoryFlagChange> Apply(ItemPatcher rooms, RoomArchive archive, RandoData data, ItemPlacementResult items,
        RandoSettings settings, int? swap)
    {
        var uses = PickupFlags.Uses(archive);
        var changes = new List<StoryFlagChange>();

        // Map spots, one per global id (room versions list the same pickup), and their flag now.
        int Swapped(int flag) => swap is int s ? flag == s ? RoomTweaks.CourtyardFlag : flag == RoomTweaks.CourtyardFlag ? s : flag : flag;
        var spots = Spots(uses, data, settings);
        // the spot each original flag belongs to
        var byFlag = spots.Values.Where(s => s.Flag != null).GroupBy(s => s.Flag!.Value).ToDictionary(g => g.Key, g => g.First());

        foreach (var use in uses.Where(u => !u.IsPickup))
        {
            if (!byFlag.TryGetValue(use.Flag, out var origin) || !items.ByGlobalId.ContainsKey(origin.GlobalId))
                continue;
            var info = data.Items.Get(origin.OriginalType);
            var otherRoom = origin.Listings.All(l => !SameRoom(RoomId.ParseBioRand(l.Room), use.Room));
            int target;
            string why;
            if (otherRoom && use.Flag == RoomTweaks.CourtyardFlag)
            {
                continue;   // the courtyard switch: the emblem's now
            }
            else if (otherRoom && info is { IsKey: true, IsDocument: false })
            {
                if (Holder(spots, items, origin) is not { Flag: int now })
                {
                    // a key the logic did not need is not placed (its pickup holds something else)
                    var where = Holder(spots, items, origin) == null ? "not in this seed" : "where no pickup flag says so";
                    changes.Add(new StoryFlagChange(use.Room, use.Opcode.Offset, use.Opcode.Name, use.Flag, use.Flag,
                        $"{info.Name} is {where} (unchanged)"));
                    continue;
                }
                target = Swapped(now);
                why = $"follows {info.Name}";
            }
            else
            {
                target = Swapped(use.Flag);
                why = "follows its spot";
            }
            if (target == use.Flag || !rooms.Intact(use.Room, use.Opcode))
                continue;
            PickupFlags.Retarget(rooms, use, target);
            changes.Add(new StoryFlagChange(use.Room, use.Opcode.Offset, use.Opcode.Name, use.Flag, target, why));
        }

        // Scripts that take a randomized pickup away for something done in another room.
        bool Followed(Spot s) => data.Items.Get(s.OriginalType) is { IsKey: true, IsDocument: false };
        foreach (var (room, op, flag, why) in Removals(archive, uses, spots.Values.Where(s => items.ByGlobalId.ContainsKey(s.GlobalId) && !Followed(s))))
        {
            if (!rooms.Intact(room, op))
                continue;
            rooms.Nop(room, new OffsetSpec(op.Offset, op.Offset));
            changes.Add(new StoryFlagChange(room, op.Offset, op.Name, flag, null, why));
        }
        return changes;
    }

    /// <summary>
    /// <c>set</c>s (group 7, set) that mark one of <paramref name="spots"/> taken without the
    /// player taking it, because of another room: the set is in a room that does not list the
    /// pickup (a room that lists it takes the same pickup), or it is in a room that does, inside
    /// an <c>if</c> on another room's pickup flag.
    /// </summary>
    private static IEnumerable<(RoomId Room, CvOpcode Op, int Flag, string Why)> Removals(RoomArchive archive,
        IReadOnlyList<PickupFlagUse> uses, IEnumerable<Spot> spots)
    {
        var mapped = spots.Where(s => s.Flag != null).ToLookup(s => s.Flag!.Value);
        var otherRooms = uses.Where(u => u.IsPickup).ToLookup(u => u.Flag);
        foreach (var group in uses.Where(u => !u.IsPickup && u.Opcode.Name == "set" && u.Opcode.Bytes.Span[5] == 0).GroupBy(u => u.Room))
        {
            List<CvOpcode>? ops = null;
            foreach (var set in group)
            {
                var spot = mapped[set.Flag].FirstOrDefault();
                if (spot == null)
                    continue;
                var listings = spot.Listings.Select(l => RoomId.ParseBioRand(l.Room)).ToList();
                if (listings.All(r => !SameRoom(r, set.Room)))
                {
                    yield return (set.Room, set.Opcode, set.Flag, $"keeps {listings[0].Name}'s pickup there (taken away when this room starts)");
                    continue;
                }
                ops ??= CvScriptReader.ReadOpcodes(archive.ReadRoom(group.Key)).ToList();
                var conditions = IfConditions(ops, set.Opcode.Offset);
                // (a check of the pickup's own flag is its own pickup: RM_7180 takes RM_3130's Bow Gun)
                var other = conditions.FirstOrDefault(c => c.Name == "ck" && c.Bytes.Span[1] == 7 &&
                    ((c.Bytes.Span[2] | c.Bytes.Span[3] << 8) & 0x3FF) is var f && f != set.Flag &&
                    otherRooms[f] is var setters && setters.Any() && setters.All(p => !SameRoom(p.Room, set.Room)));
                if (other.Bytes.Length != 0)
                {
                    var by = otherRooms[(other.Bytes.Span[2] | other.Bytes.Span[3] << 8) & 0x3FF].First();
                    yield return (set.Room, set.Opcode, set.Flag, $"keeps its pickup when {by.Room.Name}'s (area {by.Area}) is taken");
                }
            }
        }
    }

    /// <summary>The checks of every <c>if</c> around an opcode (an <c>if</c>'s block ends 2 + its length byte after it).</summary>
    private static List<CvOpcode> IfConditions(List<CvOpcode> ops, int offset)
    {
        var result = new List<CvOpcode>();
        var at = ops.FindIndex(o => o.Offset == offset);
        for (var i = at - 1; i >= 0; i--)
        {
            var o = ops[i];
            if (o.Name != "if" || o.Procedure != ops[at].Procedure || o.Offset + 2 + o.Bytes.Span[1] <= offset)
                continue;
            for (var j = i + 1; j < at && ops[j].Name is "ck" or "cmpb" or "cmpw"; j++)
                result.Add(ops[j]);
        }
        return result;
    }

    /// <summary>The spot that holds <paramref name="origin"/>'s item now (the same one among copies of it, by order).</summary>
    private static Spot? Holder(Dictionary<int, Spot> spots, ItemPlacementResult items, Spot origin)
    {
        var type = origin.OriginalType;
        var before = spots.Values.Where(s => s.OriginalType == type && items.ByGlobalId.ContainsKey(s.GlobalId)).OrderBy(s => s.GlobalId).ToList();
        var now = spots.Values.Where(s => items.ByGlobalId.TryGetValue(s.GlobalId, out var a) && a.Type == type).OrderBy(s => s.GlobalId).ToList();
        var index = before.FindIndex(s => s.GlobalId == origin.GlobalId);
        return index >= 0 && index < now.Count ? now[index] : now.FirstOrDefault();
    }

    /// <summary>The item flag a map pickup sets when taken: its trigger area's <c>ck_item</c> (preferred) or <c>set_ck_item</c>.</summary>
    private static int? FlagOf(IReadOnlyList<PickupFlagUse> uses, string roomKey, int id)
    {
        if (ScriptedPickupFlags.TryGetValue((roomKey, id), out var scripted))
            return scripted;
        var room = RoomId.ParseBioRand(roomKey);
        var flags = uses.Where(u => u.IsPickup && u.Room == room && u.Area == id)
            .OrderBy(u => u.Opcode.Name == "ck_item" ? 0 : 1).ThenBy(u => u.Flag).Select(u => u.Flag).ToList();
        return flags.Count == 0 ? null : flags[0];
    }

    /// <summary>Versions of one room (RM_0030 and RM_0031).</summary>
    private static bool SameRoom(RoomId a, RoomId b) => a.Stage == b.Stage && a.Room == b.Room;
}
