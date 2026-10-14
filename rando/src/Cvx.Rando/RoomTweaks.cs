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

        Nop("1010", 0x3EF2C, TweakMode.ItemsOnly, "always load RM_0021 (briefcase and medal)"),
        Nop("1010", 0x3EF38, 0x3EF4C, TweakMode.ItemsOnly, "always load RM_0021 (briefcase and medal)"),
        Nop("1010", 0x3EF50, 0x3EF5A, TweakMode.ItemsOnly, "always load RM_0021 (briefcase and medal)"),
        Nop("1050", 0x1DF2AA, 0x1DF2BE, TweakMode.ItemsOnly, "always load RM_0031"),
        Nop("1050", 0x1DF2C2, 0x1DF2CC, TweakMode.ItemsOnly, "always load RM_0031"),

        Nop("1070", 0x1819AE, TweakMode.Always, "the window cutscene plays when the item is taken"),

        Flag("20E0", 19, 0, "story flag for random doors"),
        Flag("5040", 19, 1, "story flag for random doors"),

        Nop("3050", 0x15F288, 0x15F2DA, TweakMode.Always, "no Steve/Alfred cutscene"),
        Nop("3050", 0x15EEDC, 0x15EEF6, TweakMode.Always, "no Steve/Alfred cutscene"),

        Patch("3060", 0x70A10 + 6, 0x00, TweakMode.ItemsOnly, "no softlock entering by the ladder without the Silver Key"),

        Patch("4080", 0x9F86C + 2, 0xC5, TweakMode.ItemsOnly, "RM_4011 right after the Alfred cutscene"),
        Patch("40F0", 0x7241C + 2, 0xC5, TweakMode.ItemsOnly, "RM_4011 right after the Alfred cutscene"),
        Flag("4080", 211, 0, "story flag for random doors"),

        Nop("5000", 0x187778, 0x18777A, TweakMode.Always, "Steve is at the airport"),
        Nop("5000", 0x187784, 0x18779C, TweakMode.Always, "Steve is at the airport"),

        Flag("6000", 35, 0, "story flag for random doors"),
        Flag("6000", 261, 0, "story flag for random doors"),

        Nop("70A0", 0x1F3140, TweakMode.DoorsOnly, "no switch to Chris here"),

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
