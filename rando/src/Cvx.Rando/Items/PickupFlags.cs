using System.Runtime.CompilerServices;
using Cvx.Formats;
using Cvx.Formats.Script;

namespace Cvx.Rando.Items;

/// <summary>An opcode that names an item flag (<c>it_flg</c>): a pickup's own or a check of one.</summary>
/// <param name="IsPickup"><c>ck_item</c> or <c>set_ck_item</c> (the pickup that sets the flag when taken).</param>
/// <param name="Area">For a pickup, its trigger area.</param>
public sealed record PickupFlagUse(RoomId Room, CvOpcode Opcode, int Flag, bool IsPickup, int Area);

/// <summary>
/// Where the game's scripts use item flags (<c>it_flg</c>, flag group 7). Every pickup sets its
/// own flag when taken (<c>ck_item</c>, event.c <c>bhItmCk</c>: <c>[op][x][flag u16][area][x]</c>;
/// <c>set_ck_item</c> hides it once taken), and many rooms read other rooms' pickup flags as
/// story progress (<c>ck</c>/<c>set</c> with group 7, flag at byte 2). Read from the original
/// rooms, once per archive.
/// </summary>
public static class PickupFlags
{
    // (Lazy: callers on several threads at once must not each read every room)
    private static readonly ConditionalWeakTable<RoomArchive, Lazy<IReadOnlyList<PickupFlagUse>>> Cache = [];

    public static IReadOnlyList<PickupFlagUse> Uses(RoomArchive archive) =>
        Cache.GetValue(archive, a => new Lazy<IReadOnlyList<PickupFlagUse>>(() => Read(a))).Value;

    private static IReadOnlyList<PickupFlagUse> Read(RoomArchive archive)
    {
        // rooms in parallel (expanding them is the slow part), kept in room order
        var rooms = new List<PickupFlagUse>[RoomTable.Count];
        Parallel.For(0, RoomTable.Count, i =>
        {
            var id = RoomTable.Get(i);
            var uses = rooms[i] = [];
            foreach (var o in CvScriptReader.ReadOpcodes(archive.ReadRoom(id)))
            {
                var b = o.Bytes.Span;
                // (the opcode's bytes are copied: the expanded room is not kept)
                var op = o with { Bytes = o.Bytes.ToArray() };
                if (o.Name is "ck_item" or "set_ck_item" && b.Length >= 6)
                    uses.Add(new PickupFlagUse(id, op, (b[2] | b[3] << 8) & 0x3FF, true, b[4]));
                else if (o.Name is "ck" or "set" && b.Length >= 6 && b[1] == 7)
                    uses.Add(new PickupFlagUse(id, op, (b[2] | b[3] << 8) & 0x3FF, false, -1));
            }
        });
        return rooms.SelectMany(r => r).ToList();
    }

    /// <summary>Points an opcode at another flag (the flag's 10 bits of its 16-bit word at byte 2).</summary>
    public static void Retarget(ItemPatcher rooms, PickupFlagUse use, int flag)
    {
        var b = use.Opcode.Bytes.Span;
        var word = (b[2] | b[3] << 8) & ~0x3FF | flag;
        rooms.PatchByte(use.Room, use.Opcode.Offset + 2, (byte)word);
        rooms.PatchByte(use.Room, use.Opcode.Offset + 3, (byte)(word >> 8));
    }
}
