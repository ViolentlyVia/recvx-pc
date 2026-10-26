// Room script changes after BioRand's ReCvDoorHelper.Begin and DoorRandomiser.FinalChecks
// (MIT, Copyright (c) 2022-2026 Ted John). Offsets are into the expanded room files.

using Cvx.Formats;
using Cvx.Formats.Script;
using Cvx.Rando.Data;
using Cvx.Rando.Items;

namespace Cvx.Rando;

public enum TweakMode { Always, ItemsOnly, DoorsOnly }

/// <summary>
/// A script change: blank opcodes (<see cref="Nop"/>), change one byte (<see cref="Patch"/>) or
/// set a story flag when the room starts (<see cref="Flag"/>).
/// </summary>
public sealed record RoomTweak(string Room, TweakMode Mode, string Why)
{
    public OffsetSpec? Nop { get; init; }
    public (int Offset, byte Value)? Patch { get; init; }
    /// <summary>A <c>set</c> opcode: flag group, flag index, operation (0 set, 1 clear).</summary>
    public (byte Group, int Index, byte Value)? Flag { get; init; }
    /// <summary>Skip the nop when it would blank a door (set_door_call) or room case change.</summary>
    public bool KeepDoors { get; init; }

    public bool AppliesTo(RandoSettings settings) => Mode switch
    {
        TweakMode.ItemsOnly => !settings.RandomDoors,
        TweakMode.DoorsOnly => settings.RandomDoors,
        _ => true,
    };

    /// <summary>The opcode bytes of <see cref="Flag"/> (<c>set</c>, 0x05).</summary>
    public byte[] FlagOpcode() =>
        Flag is { } f ? [0x05, f.Group, (byte)f.Index, (byte)(f.Index >> 8), 0x00, f.Value] : [];
}

public static class RoomTweaks
{
    private static readonly byte[] DoorOpcodes = [CvOpcodes.SetDoorCall, CvOpcodes.RoomCaseNo];

    private static RoomTweak Nop(string room, int begin, int end, TweakMode mode, string why) =>
        new(room, mode, why) { Nop = new OffsetSpec(begin, end) };
    private static RoomTweak Nop(string room, int offset, TweakMode mode, string why) => Nop(room, offset, offset, mode, why);
    private static RoomTweak Patch(string room, int offset, byte value, TweakMode mode, string why) =>
        new(room, mode, why) { Patch = (offset, value) };
    private static RoomTweak Flag(string room, int index, byte value, string why) =>
        new(room, TweakMode.DoorsOnly, why) { Flag = (1, index, value) };

    /// <summary>BioRand's list (ReCvDoorHelper.Begin).</summary>
    public static readonly IReadOnlyList<RoomTweak> Fixed =
    [
        Nop("1000", 0x18CD7A, TweakMode.Always, "do not lose the lighter when giving Rodrigo the medicine"),
        Nop("1000", 0x18DB74, TweakMode.Always, "do not lose the lighter when giving Rodrigo the medicine"),
        Nop("1000", 0x18CD74, TweakMode.Always, "do not put Rodrigo's gift into the special slot"),
        Nop("1000", 0x18DB7C, TweakMode.Always, "do not put Rodrigo's gift into the special slot"),

        // Not ported: BioRand also made RM_0010 and RM_0050 always load RM_0021 and RM_0031. The
        // game loads the event versions (RM_0020: the explosion and zombies rising from the
        // graves; RM_0030) until the pickup in RM_0080 is taken (item flag 4: `ck 07:4` with
        // value 1 is true while the flag is clear) and RM_0021/RM_0031 after; forcing the later
        // versions skipped those events. See TieCourtyardToEmblem.

        Nop("1070", 0x1819AE, TweakMode.Always, "the window cutscene plays when the item is taken"),

        Flag("20E0", 19, 0, "story flag for random doors"),
        // The nop above RM_1100's door event (room data) removes the cutscene that sets this map
        // flag, and only with it does the room set up its door to RM_2000 (RM_2000 sets it too).
        new("20A0", TweakMode.DoorsOnly, "RM_1100's door to RM_2000 works without its cutscene") { Flag = (8, 0x46, 0) },
        Flag("5040", 19, 1, "story flag for random doors"),

        Nop("3050", 0x15F288, 0x15F2DA, TweakMode.Always, "no Steve/Alfred cutscene"),
        Nop("3050", 0x15EEDC, 0x15EEF6, TweakMode.Always, "no Steve/Alfred cutscene"),

        Patch("3060", 0x70A10 + 6, 0x00, TweakMode.ItemsOnly, "no softlock entering by the ladder without the Silver Key"),

        Patch("4080", 0x9F86C + 2, 0xC5, TweakMode.ItemsOnly, "RM_4011 right after the Alfred cutscene"),
        // RM_3210's pickup (area 9) relies on the script the door mode nop removes to switch it
        // off once taken (the item opcodes only hide the model): let the game switch it off.
        Patch("4150", 0x145DF8 + 5, 0x00, TweakMode.DoorsOnly, "set_ck_item switches the taken pickup off"),
        Patch("4150", 0x145E6E + 5, 0x00, TweakMode.DoorsOnly, "ck_item switches the pickup off when taken"),
        Patch("40F0", 0x7241C + 2, 0xC5, TweakMode.ItemsOnly, "RM_4011 right after the Alfred cutscene"),
        // RM_3150's gate (wall 27) parts the room: door 1 (to RM_3011) on one side, doors 0 and 2
        // on the other. Its first-visit event (area 2, while flags 0xE5 and 0x20 are clear) closes
        // the gate until RM_3100's power and the panel open it. The door mode nop that removes the
        // event also holds area 1's door call, so it is skipped; with 0xE5 set the event never
        // runs, the gate stays open and areas 0 and 2 are doors (owner's seed 86426).
        Flag("40F0", 0xE5, 0, "the gate stays open"),
        Flag("4080", 211, 0, "story flag for random doors"),

        Nop("5000", 0x187778, 0x18777A, TweakMode.Always, "Steve is at the airport"),
        Nop("5000", 0x187784, 0x18779C, TweakMode.Always, "Steve is at the airport"),

        Flag("6000", 35, 0, "story flag for random doors"),
        Flag("6000", 261, 0, "story flag for random doors"),

        // Random doors: one character plays the whole run (player_change_set).
        Nop("70A0", 0x1F3140, TweakMode.DoorsOnly, "no switch to Chris here"),
        Nop("A1C0", 0x1BBAE2, TweakMode.DoorsOnly, "no switch to Chris here"),
        Nop("A200", 0x18753C, TweakMode.DoorsOnly, "no switch to Claire here"),

        Nop("8170", 0x14D26E, TweakMode.Always, "do not put Rodrigo's gift into the special slot"),
        Nop("8170", 0x14EB68, TweakMode.Always, "do not put Rodrigo's gift into the special slot"),

        Flag("8080", 309, 1, "story flag for random doors"),
        Flag("8080", 41, 0, "story flag for random doors"),
        Flag("80A0", 354, 0, "story flag for random doors"),
    ];

    /// <summary>
    /// Every change for these settings: the list above, and with random doors each room's
    /// door-mode nop ranges. BioRand's ranges also blank scripted doors, which it rewrote
    /// another way; recvx-pc redirects doors in the engine and needs them, so ranges with a
    /// door in them are skipped (the rest silence music and event cues of the story order).
    /// </summary>
    public static IEnumerable<RoomTweak> For(RandoSettings settings, RandoData data)
    {
        foreach (var t in Fixed.Where(t => t.AppliesTo(settings)))
            yield return t;
        if (!settings.RandomDoors)
            yield break;
        foreach (var (key, room) in data.Map.Rooms.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            foreach (var spec in room.DoorRando ?? [])
            {
                foreach (var nop in spec.Nop ?? [])
                    yield return new RoomTweak(key, TweakMode.DoorsOnly, "events that random doors skip (room data)") { Nop = nop, KeepDoors = true };
            }
        }
    }

    /// <summary>
    /// The side pack from the start: the status screen gives the bigger inventory while event
    /// flag 6 (Claire) or 7 (Chris) is set (player.c <c>bhCheckSubPack</c>). Set in the room a
    /// new game starts in (RM_0000, with random doors too).
    /// </summary>
    public static readonly IReadOnlyList<RoomTweak> SidePack =
    [
        new("1000", TweakMode.Always, "start with the side pack") { Flag = (1, 6, 0) },
        new("1000", TweakMode.Always, "start with the side pack") { Flag = (1, 7, 0) },
    ];

    /// <summary>The Sp Alloy Emblem, which opens the courtyard's gate (RM_0031, to RM_0100).</summary>
    public const int SpAlloyEmblem = 86;
    /// <summary>The item flag by which RM_0010 and RM_0050 load the courtyard's gate version.</summary>
    public const int CourtyardFlag = 4;

    /// <summary>
    /// The courtyard's gate (RM_0031's script door to RM_0100) opens with the Sp Alloy Emblem, but
    /// the game loads that version of the courtyard only once item flag 4 is set: the pickup in
    /// RM_0080 (the Padlock Key's spot). Before it, the courtyard is RM_0030, where the gate does
    /// nothing ("There's no need to use it now"), so a seed that puts the emblem before that
    /// pickup could strand the player at the gate (owner's seed 300378). The pickup holding the
    /// emblem takes flag 4 and RM_0080's takes the emblem spot's flag: holding the emblem is
    /// what switches the courtyard. The rooms' other uses of the two flags are left to
    /// <see cref="StoryFlags.Apply"/>.
    /// </summary>
    /// <returns>The emblem spot's flag that changed, or null when nothing changed.</returns>
    public static int? TieCourtyardToEmblem(ItemPatcher rooms, RoomArchive archive, RandoData data, ItemPlacementResult items, RandoSettings settings)
    {
        // the emblem's pickup: its room and trigger area
        var spot = data.Map.Rooms
            .SelectMany(r => (r.Value.Items ?? []).Select(i => (Room: r.Key, Item: i)))
            .FirstOrDefault(x => x.Item.GlobalId is int g && (x.Item.DoorRando is not bool dr || dr == settings.RandomDoors) &&
                                 items.ByGlobalId.TryGetValue(g, out var a) && a.Type == SpAlloyEmblem);
        if (spot.Item == null)
            return null;
        var uses = PickupFlags.Uses(archive);
        var room = RoomId.ParseBioRand(spot.Room);
        var pickup = uses.FirstOrDefault(u => u.IsPickup && u.Room == room && u.Area == spot.Item.Id);
        if (pickup == null || pickup.Flag == CourtyardFlag)
            return null;   // given by a script, or already in RM_0080's spot
        var flag = pickup.Flag;
        foreach (var use in uses.Where(u => u.IsPickup))
        {
            if (use.Flag == flag)
                PickupFlags.Retarget(rooms, use, CourtyardFlag);
            else if (use.Flag == CourtyardFlag)
                PickupFlags.Retarget(rooms, use, flag);
        }
        return flag;
    }

    public static void ApplySidePack(ItemPatcher rooms)
    {
        foreach (var t in SidePack)
            rooms.AddStartOpcodes(RoomId.ParseBioRand(t.Room), t.FlagOpcode());
    }

    public static void Apply(ItemPatcher rooms, RandoSettings settings, RandoData data)
    {
        foreach (var t in For(settings, data))
        {
            var id = RoomId.ParseBioRand(t.Room);
            if (t.Nop is { } nop && !(t.KeepDoors && rooms.RangeHas(id, nop, DoorOpcodes)))
                rooms.Nop(id, nop);
            if (t.Patch is { } patch)
                rooms.PatchByte(id, patch.Offset, patch.Value);
            if (t.Flag != null)
                rooms.AddStartOpcodes(id, t.FlagOpcode());
        }
    }
}
