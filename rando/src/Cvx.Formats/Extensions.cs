using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Cvx.Formats;

internal static class Extensions
{
    /// <summary>
    /// Reads <paramref name="count"/> values at <paramref name="offset"/>, padding with zeros
    /// past the end of the data.
    /// </summary>
    public static ReadOnlySpan<T> GetSafeSpan<T>(this ReadOnlyMemory<byte> memory, int offset, int count) where T : unmanaged
    {
        var required = count * Unsafe.SizeOf<T>();
        if (memory.Length - offset >= required && offset >= 0)
            return MemoryMarshal.Cast<byte, T>(memory.Span.Slice(offset, required));

        var copy = new byte[required];
        if (offset < memory.Length && offset >= 0)
            memory.Span[offset..].CopyTo(copy);
        return MemoryMarshal.Cast<byte, T>(copy);
    }

    public static void WriteStruct<T>(this BinaryWriter bw, T value) where T : unmanaged =>
        bw.Write(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in value)));

    public static void WriteStructs<T>(this BinaryWriter bw, IEnumerable<T> values) where T : unmanaged
    {
        foreach (var value in values)
            bw.WriteStruct(value);
    }

    public static void Align(this BinaryWriter bw, int alignment)
    {
        bw.Flush();
        var stream = bw.BaseStream;
        while (stream.Position % alignment != 0)
            bw.Write((byte)0);
    }
}
