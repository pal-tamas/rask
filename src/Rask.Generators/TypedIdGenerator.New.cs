using System.Text;

namespace Rask.Generators;

public sealed partial class TypedIdGenerator
{
    // New() of an id over a Guid: a version-7 Guid that is strictly increasing within the process (RFC 9562 §6.2,
    // method 1). The 48-bit Unix millisecond and a 12-bit counter in rand_a are one 60-bit tick, advanced by a CAS
    // loop: a new millisecond restarts the counter at 0, a busy one adds 1, and a full one (4096 ids) or a clock
    // that stepped back carries into the next millisecond, running the stamp ahead of the wall clock rather than
    // repeating or going back. rand_b stays the 62 random bits of a v4 Guid, so ids of two processes never meet.
    // Guid.CreateVersion7() alone fills rand_a at random, so two ids in one millisecond come out in either order.
    private static void AppendNew(StringBuilder sb, string name)
    {
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    ///     A new id: a version-7 <see cref=\"global::System.Guid\" />, time-ordered and strictly increasing within");
        sb.AppendLine("    ///     this process (RFC 9562's counter in <c>rand_a</c>), so the key's index stays append-only.");
        sb.AppendLine("    /// </summary>");
        sb.Append("    public static ").Append(name).AppendLine(" New()");
        sb.AppendLine("    {");
        sb.AppendLine("        long last, tick;");
        sb.AppendLine("        do");
        sb.AppendLine("        {");
        sb.AppendLine("            last = global::System.Threading.Volatile.Read(ref __lastTick);");
        sb.AppendLine("            tick = global::System.Math.Max(global::System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() << 12, last + 1);");
        sb.AppendLine("        }");
        sb.AppendLine("        while (global::System.Threading.Interlocked.CompareExchange(ref __lastTick, tick, last) != last);");
        sb.AppendLine();
        sb.AppendLine("        global::System.Span<byte> bytes = stackalloc byte[16];");
        sb.AppendLine("        global::System.Guid.NewGuid().TryWriteBytes(bytes, bigEndian: true, out _);");
        sb.AppendLine("        global::System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, (tick >> 12 << 16) | 0x7000L | (tick & 0xFFFL));");
        sb.AppendLine("        return new(new global::System.Guid(bytes, bigEndian: true));");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private static long __lastTick;");
        sb.AppendLine();
    }
}
