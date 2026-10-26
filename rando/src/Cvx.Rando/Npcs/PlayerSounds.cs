using System.Buffers.Binary;
using Cvx.Formats;
using Cvx.Rando.Music;

namespace Cvx.Rando.Npcs;

/// <summary>
/// The players' pain and death sounds. Each player has a sound pack of their own, CORE_00n.SPQ
/// in MULTSPQ1.AFS and MULTSPQ2.AFS (key 0xFFF0 | player; sdfunc.c <c>RequestPlayerVoiceSoundBank</c>),
/// with one shot bank (type 2, bank 4): <c>CallPlayerVoice</c>'s sounds 1024-1027 are its programs
/// 0-3, each one 32 kHz sample: 1025 (program 1) the death (player.c <c>bhCPM0_die</c>), the
/// others hurt (<c>bhCPM0_damage</c>). A shot bank is <c>[hd offset, hd size, bd offset, bd size]</c>
/// and the HD (programs, sample sets, samples, VAG list) and BD (SPU ADPCM) they point to
/// (ps2_sg_sd.c <c>sdBankDownload</c>; pc_iop_tsnddrv.c <c>note_on</c>).
/// </summary>
public static class PlayerSounds
{
    public const int BankType = 2;
    public const int BankNo = 4;

    /// <summary>The new samples' rate: lower than the game's 32 kHz, so the bank does not grow much.</summary>
    public const int Rate = 22050;

    /// <summary>Program -> BioRand's hurt clip (0-2 hurt, 3 death): program 1 is the death.</summary>
    private static readonly int[] ClipOf = [0, 3, 1, 2];

    public static ushort KeyOf(string player) => (ushort)(0xFFF0 | (player == "Chris" ? 1 : 0));

    /// <summary>
    /// The pack with its programs' samples made from <paramref name="clips"/> (BioRand's hurt
    /// number -> the clip's file bytes and name): mono at <see cref="Rate"/>, as loud as the sample
    /// each replaces, a hurt at most 1.2 s and the death 3 s. A program whose clip is missing keeps its sample.
    /// </summary>
    public static byte[] Replace(byte[] pack, IReadOnlyDictionary<int, (byte[] Data, string Name)> clips)
    {
        var banks = SoundPacks.Parse(pack);
        var at = banks.FindIndex(b => b.Type == BankType && b.BankNo == BankNo);
        if (at < 0)
            throw new InvalidDataException("The player's sound pack has no bank 4.");
        var bank = banks[at].Data.ToArray();
        int U32(byte[] b, int p) => (int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p));
        var (hdAt, hdSize, bdAt, bdSize) = (U32(bank, 0), U32(bank, 4), U32(bank, 8), U32(bank, 12));
        var hd = bank.AsSpan(hdAt, hdSize).ToArray();
        var bd = bank.AsSpan(bdAt, bdSize);

        using var body = new MemoryStream();
        foreach (var (program, vag) in Programs(hd))
        {
            var oldAt = U32(hd, vag);
            var old = Vag.Decode(bd, oldAt);
            byte[] data;
            int rate;
            if (program < ClipOf.Length && clips.TryGetValue(ClipOf[program], out var clip))
            {
                var (pcm, clipRate) = Decode(clip.Data, clip.Name);
                pcm = Resample(pcm, clipRate, Rate);
                var max = (int)(Rate * (ClipOf[program] == 3 ? 3.0 : 1.2));
                if (pcm.Length > max)
                    pcm = pcm[..max];
                var (rms, peak) = MusicLibrary.Level([pcm]);
                var gain = MusicLibrary.MatchGain(rms, peak, MusicLibrary.Level([old]).Rms);
                for (var i = 0; i < pcm.Length; i++)
                    pcm[i] = (short)Math.Clamp(Math.Round(pcm[i] * gain), short.MinValue, short.MaxValue);
                data = Vag.Encode(pcm);
                rate = Rate;
            }
            else
            {
                // the game's: its blocks up to the end flag
                var end = oldAt;
                while (end + Vag.BlockSize <= bd.Length && (bd[end + 1] & 1) == 0)
                    end += Vag.BlockSize;
                data = bd[oldAt..Math.Min(bd.Length, end + Vag.BlockSize)].ToArray();
                rate = BinaryPrimitives.ReadUInt16LittleEndian(hd.AsSpan(vag + 4));
            }
            BinaryPrimitives.WriteUInt32LittleEndian(hd.AsSpan(vag), (uint)body.Position);
            BinaryPrimitives.WriteUInt16LittleEndian(hd.AsSpan(vag + 4), (ushort)rate);
            body.Write(data);
        }

        // the bank as the game's are laid out: the head, the HD where it was, the BD where it was
        var newBd = body.ToArray();
        var result = new byte[bdAt + newBd.Length];
        bank.AsSpan(0, bdAt).CopyTo(result);
        hd.CopyTo(result.AsSpan(hdAt));
        newBd.CopyTo(result.AsSpan(bdAt));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), (uint)newBd.Length);
        banks[at] = new SoundBank(BankType, BankNo, result);
        return SoundPacks.Build(banks);
    }

    /// <summary>Each program's VAG entry (its first split's sample), in program order.</summary>
    public static List<(int Program, int Vag)> Programs(byte[] hd)
    {
        int U32(int p) => (int)BinaryPrimitives.ReadUInt32LittleEndian(hd.AsSpan(p));
        int U16(int p) => BinaryPrimitives.ReadUInt16LittleEndian(hd.AsSpan(p));
        int Chunk(int field) => U32(0x10 + field);
        int Entry(int chunk, int i)
        {
            if (chunk < 0 || chunk + 16 > hd.Length || i > U32(chunk + 12))
                return -1;
            var e = BinaryPrimitives.ReadUInt32LittleEndian(hd.AsSpan(chunk + 16 + 4 * i));
            return e == 0xFFFFFFFF ? -1 : chunk + (int)e;
        }
        var (prog, sset, smp, vag) = (Chunk(0x14), Chunk(0x18), Chunk(0x1C), Chunk(0x20));
        var result = new List<(int, int)>();
        for (var p = 0; p <= U32(prog + 12); p++)
        {
            var at = Entry(prog, p);
            if (at < 0 || hd[at + 4] == 0)
                continue;
            var split = at + U32(at);
            var ss = Entry(sset, U16(split));
            var s = ss < 0 ? -1 : Entry(smp, U16(ss + 4));
            var v = s < 0 ? -1 : Entry(vag, U16(s));
            if (v >= 0 && !result.Any(r => r.Item2 == v))
                result.Add((p, v));
        }
        return result;
    }

    /// <summary>An OGG or 16-bit PCM WAV as mono samples and its rate.</summary>
    public static (short[] Pcm, int Rate) Decode(byte[] data, string name)
    {
        if (name.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
        {
            var (channels, rate) = MusicLibrary.DecodeOgg(data);
            return (channels[0].Select((l, i) => (short)((l + channels[1][i]) / 2)).ToArray(), rate);
        }
        // RIFF WAVE: the "fmt " and "data" chunks (BioRand's are all 16-bit PCM)
        int p = 12, channelCount = 1, wavRate = 22050, bits = 16;
        ReadOnlySpan<byte> samples = [];
        while (p + 8 <= data.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(data, p, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p + 4));
            if (id == "fmt ")
            {
                channelCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p + 10));
                wavRate = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p + 12));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p + 22));
            }
            else if (id == "data")
            {
                samples = data.AsSpan(p + 8, Math.Min(size, data.Length - p - 8));
                break;
            }
            p += 8 + size + (size & 1);
        }
        if (bits != 16)
            throw new InvalidDataException($"{name}: {bits}-bit WAV, only 16-bit is read.");
        var frames = samples.Length / (2 * channelCount);
        var mono = new short[frames];
        for (var i = 0; i < frames; i++)
        {
            var sum = 0;
            for (var c = 0; c < channelCount; c++)
                sum += BinaryPrimitives.ReadInt16LittleEndian(samples[((i * channelCount + c) * 2)..]);
            mono[i] = (short)(sum / channelCount);
        }
        return (mono, wavRate);
    }

    /// <summary>Linear resampling (screams and grunts; nothing finer is needed).</summary>
    public static short[] Resample(short[] pcm, int from, int to)
    {
        if (from == to || pcm.Length == 0)
            return pcm;
        var result = new short[(int)((long)pcm.Length * to / from)];
        for (var i = 0; i < result.Length; i++)
        {
            var x = (double)i * from / to;
            var k = (int)x;
            var f = x - k;
            var a = pcm[Math.Min(k, pcm.Length - 1)];
            var b = pcm[Math.Min(k + 1, pcm.Length - 1)];
            result[i] = (short)Math.Round(a + (b - a) * f);
        }
        return result;
    }
}
