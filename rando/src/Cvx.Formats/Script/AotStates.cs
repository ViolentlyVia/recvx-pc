using Cvx.Formats.Rdx;

namespace Cvx.Formats.Script;

/// <summary>One possible state of a trigger area: from the room file, or written by a script.</summary>
public readonly record struct AotState(int Index, byte Type, uint Attr, byte Prm0, byte Prm1, byte Prm2, byte Prm3, int? ScriptOffset)
{
    public bool FromScript => ScriptOffset != null;

    public string Describe()
    {
        var where = ScriptOffset is int o ? $"script 0x{o:X}" : "room";
        return Type switch
        {
            RdxAot.KindDoor => $"door to stage {Prm0} room {Prm1} entrance {Prm2} ({where})",
            RdxAot.KindItem => $"item slot {Prm0} ({where})",
            _ => $"type {Type} ({where})",
        };
    }
}

/// <summary>
/// The trigger areas (<c>etcp</c>) of a room with every state its scripts can give them through
/// <c>set_etc_atari2</c>. Which script write applies depends on game flags, so all are kept.
/// </summary>
public sealed class AotStates
{
    private readonly Dictionary<int, List<AotState>> _states = [];

    public int StaticCount { get; private init; }

    public IReadOnlyList<AotState> Get(int index) => _states.TryGetValue(index, out var s) ? s : [];

    public static AotStates Read(RdxFile room, IReadOnlyList<CvOpcode> opcodes)
    {
        var aots = room.Aots;
        var result = new AotStates { StaticCount = aots.Length };
        for (var i = 0; i < aots.Length; i++)
        {
            var a = aots[i];
            result.Add(new AotState(i, a.Kind, (uint)a.Flags, a.Stage, a.Room, a.ExitId, a.Transition, null));
        }

        foreach (var op in opcodes)
        {
            if (op.Opcode != CvOpcodes.SetEtcAtari2 || op.Length < 9)
                continue;
            var b = op.Bytes.Span;
            result.Add(new AotState(b[1], b[8], (uint)(b[2] | b[3] << 8), b[4], b[5], b[6], b[7], op.Offset));
        }
        return result;
    }

    private void Add(AotState state)
    {
        if (!_states.TryGetValue(state.Index, out var list))
            _states[state.Index] = list = [];
        list.Add(state);
    }
}
