namespace Cvx.Formats.Rdx;

/// <summary>
/// A readable summary of a room file: what <c>dump-room</c> prints, and the data later phases
/// check BioRand's room data against.
/// </summary>
public sealed record RoomSummary(
    string Room,
    string BioRandId,
    int CompressedSize,
    int ExpandedSize,
    float Version,
    string Author,
    RoomCounts Counts,
    IReadOnlyList<EnemySummary> Enemies,
    IReadOnlyList<ItemSummary> Items,
    IReadOnlyList<AotSummary> Aots,
    IReadOnlyList<EntrySummary> Entrances,
    int ModelPages,
    int MotionPages,
    int TextureGroups,
    int ScriptProcedures,
    int ScriptOffset,
    int ScriptLength)
{
    public static unsafe RoomSummary Create(RoomId id, RdxFile file, int compressedSize)
    {
        var header = file.Header;
        string author;
        {
            var bytes = new byte[32];
            for (var i = 0; i < 32; i++)
                bytes[i] = header.Author[i];
            author = System.Text.Encoding.ASCII.GetString(bytes).TrimEnd('\0');
        }

        var enemies = new List<EnemySummary>();
        var e = file.Enemies;
        for (var i = 0; i < e.Length; i++)
        {
            enemies.Add(new EnemySummary(i, e[i].Type, e[i].Variant, e[i].Effect, e[i].Index, e[i].Header,
                e[i].Position.X, e[i].Position.Y, e[i].Position.Z, e[i].Rotation.Y));
        }

        var items = new List<ItemSummary>();
        var it = file.Items;
        for (var i = 0; i < it.Length; i++)
        {
            items.Add(new ItemSummary(i, it[i].ItemId, it[i].Type, it[i].Flags, it[i].Floor, it[i].ModelVersion,
                it[i].Position.X, it[i].Position.Y, it[i].Position.Z));
        }

        var aots = new List<AotSummary>();
        var a = file.Aots;
        var aotTable = file.TableOffsetOf(RdxTable.Aots);
        for (var i = 0; i < a.Length; i++)
        {
            var kind = a[i].Kind switch
            {
                RdxAot.KindDoor => "door",
                RdxAot.KindItem => "item",
                _ => $"kind{a[i].Kind}",
            };
            string? target = null;
            if (a[i].Kind == RdxAot.KindDoor)
            {
                var t = new RoomId(a[i].Stage, a[i].Room, 0);
                target = $"RM_{t.Stage}{t.Room:00}x ({t.Stage + 1:X1}{t.Room:X2})";
            }
            aots.Add(new AotSummary(i, kind, a[i].Kind, a[i].Flags, a[i].Stage, a[i].Room, a[i].ExitId, a[i].Transition,
                target, a[i].Position.X, a[i].Position.Y, a[i].Position.Z, aotTable + i * 36));
        }

        var entrances = new List<EntrySummary>();
        var p = file.Players;
        for (var i = 0; i < p.Length; i++)
            entrances.Add(new EntrySummary(i, p[i].Position.X, p[i].Position.Y, p[i].Position.Z, p[i].RotationY));

        return new RoomSummary(
            id.Name,
            id.BioRandId,
            compressedSize,
            file.Length,
            header.Version,
            author,
            new RoomCounts(file.Cameras.Count, file.LightingCount, e.Length, file.Objects.Length, it.Length,
                file.Effects.Length, file.Boundaries.Length, a.Length, file.Triggers.Length, p.Length,
                file.Events.Length, file.Text.Count),
            enemies,
            items,
            aots,
            entrances,
            file.Models.PageCount,
            file.Motions.PageCount,
            file.Textures.Count,
            file.Script.Count,
            file.ScriptOffset,
            file.Script.Data.Length);
    }
}

public sealed record RoomCounts(int Cameras, int Lights, int Enemies, int Objects, int Items, int Effects,
    int Boundaries, int Aots, int Triggers, int Entrances, int Events, int Messages);

public sealed record EnemySummary(int Index, int Type, int Variant, int Effect, int Number, int Flags,
    float X, float Y, float Z, int Direction);

public sealed record ItemSummary(int Index, int ItemId, int Type, uint Flags, int Floor, int ModelVersion,
    float X, float Y, float Z);

public sealed record AotSummary(int Index, string Kind, int KindValue, int Flags, int Prm0, int Prm1, int Prm2, int Prm3,
    string? DoorTarget, float X, float Y, float Z, int FileOffset);

public sealed record EntrySummary(int Index, float X, float Y, float Z, int Direction);
