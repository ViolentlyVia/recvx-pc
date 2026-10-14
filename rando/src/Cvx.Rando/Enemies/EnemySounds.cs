using Cvx.Formats;

namespace Cvx.Rando.Enemies;

/// <summary>
/// Enemy sounds: an enemy plays its sounds from bank 3 of the room's sound pack (sdfunc.c
/// <c>CallEnemySe</c> forces bank 3; the "shot" bank, type 2), which holds the sounds of the
/// enemies the room normally has. A room whose enemies changed gets the donor room's bank.
/// </summary>
public static class EnemySounds
{
    public const int BankType = 2;
    public const int BankNo = 3;

    /// <summary>
    /// Writes MULTSPQ1.AFS / MULTSPQ2.AFS to <paramref name="directory"/> with the changed rooms'
    /// packs (and removes ones from an earlier seed that are not needed). Returns the rooms changed.
    /// </summary>
    public static List<RoomId> Write(GameData data, IReadOnlyCollection<EnemyRoomChange> changes, string directory)
    {
        var done = new List<RoomId>();
        var discs = new[] { 1, 2 }.Select(d => SoundPacks.Open(data, d)).ToArray();
        try
        {
            ReadOnlyMemory<byte>? Bank(RoomId room)
            {
                foreach (var packs in discs)
                {
                    if (packs.ReadRoom(room) is { } pack &&
                        SoundPacks.Parse(pack).FirstOrDefault(b => b.Type == BankType && b.BankNo == BankNo) is { } bank)
                        return bank.Data;
                }
                return null;
            }

            foreach (var packs in discs)
            {
                var replacements = new Dictionary<int, byte[]>();
                foreach (var change in changes.Where(c => c.SoundFrom != null).OrderBy(c => c.Room))
                {
                    var index = packs.IndexOf(change.Room);
                    if (index < 0 || Bank(change.SoundFrom!.Value) is not { } sounds)
                        continue;
                    var banks = SoundPacks.Parse(packs.Afs.ReadEntry(index));
                    var at = banks.FindIndex(b => b.Type == BankType && b.BankNo == BankNo);
                    var bank = new SoundBank(BankType, BankNo, sounds);
                    if (at >= 0)
                        banks[at] = bank;
                    else
                        banks.Insert(banks.FindLastIndex(b => b.Type == BankType) + 1, bank);
                    replacements[index] = SoundPacks.Build(banks);
                    if (!done.Contains(change.Room))
                        done.Add(change.Room);
                }

                var path = Path.Combine(directory, packs.Name + ".AFS");
                if (replacements.Count != 0)
                    AfsWriter.Write(packs.Afs, replacements, path);
                else if (File.Exists(path))
                    File.Delete(path);
            }
        }
        finally
        {
            foreach (var d in discs)
                d.Dispose();
        }
        return done;
    }
}
