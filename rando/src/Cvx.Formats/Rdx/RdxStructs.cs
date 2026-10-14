// Ported from BioRand's biohazard-utils (RdtCv.cs), MIT, Copyright (c) 2022-2026 Ted John.
// Comments give the matching names from the game's own headers (recvx-decomp system.h / types.h).

using System.Runtime.InteropServices;

namespace Cvx.Formats.Rdx;

/// <summary><c>RDT_WORK</c>, the first 0x80 bytes of a room file.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct RdxHeader
{
    /// <summary><c>ver</c>; the game rejects a room below 1.72.</summary>
    public float Version;
    public int Flags;
    public int Data0;
    public int Data1;
    /// <summary><c>hed00</c>: the <c>ROM_WORK</c> table (pointers, then counts).</summary>
    public int TableOffset;
    /// <summary><c>hed01</c>: model offsets (room model, then one per enemy/object/item).</summary>
    public int ModelOffset;
    /// <summary><c>hed02</c>: enemy motions.</summary>
    public int MotionOffset;
    /// <summary><c>hed03</c>: event scripts (<c>evtp</c>).</summary>
    public int ScriptOffset;
    /// <summary><c>hed04</c>: textures.</summary>
    public int TextureOffset;
    public fixed int Unused[15];
    /// <summary><c>usrname</c>, e.g. "N-MIKAMI".</summary>
    public fixed byte Author[32];
}

/// <summary>Enemy placement (<c>ETTY_WORK</c> in <c>enep</c>).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RdxEnemy
{
    /// <summary><c>flg</c> (bit 0: has its own model entry).</summary>
    public int Header;
    /// <summary><c>id</c>: enemy type, index into <c>bhJumpEnemy</c>.</summary>
    public short Type;
    /// <summary><c>type</c>.</summary>
    public short Effect;
    /// <summary><c>flr_no</c> (low byte), <c>mdlver</c> (high byte).</summary>
    public short Variant;
    /// <summary><c>wrk_no</c> (low byte), <c>prm1</c> (high byte).</summary>
    public short Index;
    public RdxVectorF Position;
    public RdxVector32 Rotation;
}

/// <summary>Object placement (<c>ETTY_WORK</c> in <c>objp</c>).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RdxObject
{
    public int Type;
    public int Flags1;
    public int Flags2;
    public RdxVectorF Position;
    public RdxVector32 Rotation;
}

/// <summary>Item placement (<c>ETTY_WORK</c> in <c>itmp</c>).</summary>
/// <remarks>
/// The game's <c>bhSetItem</c> (objitm.c) takes the item from <see cref="ItemId"/> (<c>id</c>,
/// offset 4), and the pickup reads it from there. BioRand writes <c>type</c> (offset 6)
/// instead and patches the pickup code to read that; we write <see cref="ItemId"/>.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RdxItem
{
    /// <summary><c>flg</c>; bit 0 means the item is there to pick up.</summary>
    public uint Flags;
    /// <summary><c>id</c>: the item (index into <c>itemdata</c>).</summary>
    public ushort ItemId;
    /// <summary><c>type</c>: low byte becomes the item work's type, high byte its param.</summary>
    public ushort Type;
    /// <summary><c>flr_no</c>.</summary>
    public sbyte Floor;
    /// <summary><c>mdlver</c>.</summary>
    public byte ModelVersion;
    /// <summary><c>wrk_no</c>.</summary>
    public byte WorkNo;
    /// <summary><c>prm1</c>.</summary>
    public sbyte Param1;
    public RdxVectorF Position;
    /// <summary><c>ax</c>, <c>az</c>, <c>ay</c>, <c>aspd</c>.</summary>
    public short AngleX;
    public short AngleZ;
    public short AngleY;
    public short AngleSpeed;
    /// <summary><c>hide</c>.</summary>
    public uint Hide;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RdxEffect
{
    public int Type;
    public int Flags1;
    public int Flags2;
    public RdxVectorF Position;
    public RdxVectorF Size;
    public int Settings0;
    public int Settings1;
    public int Settings2;
    public int Settings3;
    public int Settings4;
    public int Settings5;
    public int Settings6;
    public int Settings7;
}

/// <summary>Collision area (<c>ATR_WORK</c> in <c>walp</c>).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RdxBoundary
{
    public int Type;
    public int Flags1;
    public RdxVectorF Position;
    public RdxVectorF Size;
    public int Flags2;
}

/// <summary>
/// Trigger area (<c>ATR_WORK</c> in <c>etcp</c>): doors (kind 0), item pickups (kind 4) and
/// other interactions.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RdxAot
{
    public byte Unk00;
    /// <summary><c>type</c>: 0 door, 4 item.</summary>
    public byte Kind;
    public byte Unk02;
    public byte Unk03;
    /// <summary><c>attr</c>.</summary>
    public int Flags;
    public RdxVectorF Position;
    public RdxVectorF Size;
    /// <summary><c>prm0</c>: door target stage, or item index for an item.</summary>
    public byte Stage;
    /// <summary><c>prm1</c>: door target room, or message id.</summary>
    public byte Room;
    /// <summary><c>prm2</c>: entry position in the target room.</summary>
    public byte ExitId;
    /// <summary><c>prm3</c>: door animation.</summary>
    public byte Transition;

    public const byte KindDoor = 0;
    public const byte KindItem = 4;
}

/// <summary><c>ATR_WORK</c> in <c>flrp</c>.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RdxTrigger
{
    public int Type;
    public int Attrs1;
    public RdxVectorF Position;
    public RdxVectorF Size;
    public int Attrs2;
}

/// <summary>Entry position (<c>POS</c> in <c>posp</c>).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RdxPlayer
{
    public RdxVectorF Position;
    public int RotationY;
}

/// <summary><c>ATR_WORK</c> in <c>rutp</c>.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RdxEvent
{
    public int Type;
    public int Attrs1;
    public RdxVectorF Position;
    public RdxVectorF Size;
    public int Attrs2;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct RdxReaction
{
    public uint Unk00;
    public uint Unk01;
    public fixed byte Data[2048];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RdxVectorF(float x, float y, float z)
{
    public float X = x;
    public float Y = y;
    public float Z = z;

    public override readonly string ToString() => $"({X:0.##}, {Y:0.##}, {Z:0.##})";
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RdxVector32(int x, int y, int z)
{
    public int X = x;
    public int Y = y;
    public int Z = z;

    public override readonly string ToString() => $"({X}, {Y}, {Z})";
}
