namespace Cvx.Rando;

/// <summary>
/// The randomizer's random numbers: PCG32 (O'Neill), so a seed gives the same results on every
/// machine and .NET version (System.Random makes no such promise).
/// </summary>
public sealed class Rng
{
    private ulong _state;
    private readonly ulong _inc;

    public Rng(ulong seed, ulong stream = 0)
    {
        _inc = (stream << 1) | 1;
        _state = 0;
        NextUInt();
        _state += seed;
        NextUInt();
    }

    /// <summary>A separate generator for one part of the randomizer (items, enemies...).</summary>
    public static Rng For(int seed, string part)
    {
        ulong stream = 1469598103934665603;
        foreach (var c in part)
            stream = (stream ^ c) * 1099511628211;
        return new Rng((ulong)(uint)seed, stream);
    }

    public uint NextUInt()
    {
        var old = _state;
        _state = old * 6364136223846793005UL + _inc;
        var xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        var rot = (int)(old >> 59);
        return (xorShifted >> rot) | (xorShifted << (-rot & 31));
    }

    /// <summary>0 &lt;= result &lt; <paramref name="max"/>, without modulo bias.</summary>
    public int Next(int max)
    {
        if (max <= 0)
            throw new ArgumentOutOfRangeException(nameof(max));
        var bound = (uint)max;
        var threshold = (uint)(-bound % bound);
        while (true)
        {
            var r = NextUInt();
            if (r >= threshold)
                return (int)(r % bound);
        }
    }

    /// <summary><paramref name="min"/> &lt;= result &lt; <paramref name="max"/>.</summary>
    public int Next(int min, int max) => max <= min ? min : min + Next(max - min);

    /// <summary>True with the given chance in percent.</summary>
    public bool NextProbability(int percent) => Next(100) < percent;

    public T NextOf<T>(IReadOnlyList<T> items) => items[Next(items.Count)];

    public List<T> Shuffle<T>(IEnumerable<T> items)
    {
        var list = items.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    /// <summary>Picks a key with a chance proportional to its weight.</summary>
    public T NextWeighted<T>(IReadOnlyList<(T Item, double Weight)> table)
    {
        var total = table.Sum(x => x.Weight);
        var r = NextUInt() / (double)uint.MaxValue * total;
        foreach (var (item, weight) in table)
        {
            if (r < weight)
                return item;
            r -= weight;
        }
        return table[^1].Item;
    }
}
