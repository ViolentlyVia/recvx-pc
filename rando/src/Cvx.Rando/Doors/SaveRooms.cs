using System.Runtime.InteropServices;
using Cvx.Formats;
using Cvx.Formats.Rdx;
using Cvx.Formats.Script;

namespace Cvx.Rando.Doors;

/// <summary>What a room offers for saving: an item box object and/or a typewriter.</summary>
public readonly record struct SaveRoomInfo(bool ItemBox, bool Typewriter)
{
    public bool IsSaveRoom => ItemBox && Typewriter;
}

/// <summary>
/// Finds save rooms in the room files instead of trusting a list:
/// - an item box is a room object with id 376 (found by comparing rooms: every room with the
///   typewriter script below has one);
/// - a typewriter's script opens the save screen by setting <c>cb_flg</c> bit 0x200000: the
///   <c>set</c> command with flag group 10 (<c>cb_flg</c>, event.c) and flag index 10 (indexes
///   count from the top bit).
/// </summary>
public static class SaveRooms
{
    private const int ItemBoxObject = 376;

    /// <summary>
    /// The rooms with a typewriter (BioRand ids), as <see cref="FindAll"/> finds them. Kept as a
    /// list because scanning every room takes seconds; a test checks it against the room files.
    /// </summary>
    public static readonly IReadOnlySet<string> Typewriters = new HashSet<string>(StringComparer.Ordinal)
    {
        "1010", "1040", "2080", "3070", "4040", "40B0", "5040", "6000", "6001",
        "7040", "8020", "8090", "8160", "8161", "9040", "A040", "A0E0", "A150",
    };

    public static SaveRoomInfo Inspect(RdxFile room)
    {
        var box = false;
        foreach (var o in room.Objects)
        {
            // RdxObject.Flags1 is ETTY_WORK id (low 16 bits) and type (high 16 bits)
            if ((o.Flags1 & 0xFFFF) == ItemBoxObject)
                box = true;
        }

        var typewriter = false;
        foreach (var op in CvScriptReader.ReadOpcodes(room))
        {
            var b = op.Bytes.Span;
            if (b[0] == 0x05 && b.Length >= 6 && b[1] == 10 && b[2] == 10 && b[3] == 0 && b[5] == 0)
                typewriter = true;
        }
        return new SaveRoomInfo(box, typewriter);
    }

    public static Dictionary<RoomId, SaveRoomInfo> FindAll(RoomArchive rooms)
    {
        var result = new Dictionary<RoomId, SaveRoomInfo>();
        foreach (var id in RoomTable.All)
            result[id] = Inspect(rooms.ReadRoom(id));
        return result;
    }
}
