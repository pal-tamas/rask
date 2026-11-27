using System.Buffers.Binary;

namespace Rask.Batteries;

/// <summary>
///     The key of a battery's own row: a version-7 <see cref="Guid" />, made by Rask before the insert, time-ordered and
///     strictly increasing within the process — what a typed id's generated <c>New()</c> makes, for a key that is a plain
///     <see cref="Guid" /> on a public battery entity.
/// </summary>
/// <remarks>
///     <para>
///         The 48-bit Unix millisecond and a 12-bit counter in <c>rand_a</c> are one 60-bit tick (RFC 9562 §6.2, method
///         1), advanced by a CAS loop: a new millisecond restarts the counter, a busy one adds 1, and a full one or a
///         clock that stepped back carries into the next millisecond, so the stamp runs ahead rather than repeating.
///         <c>rand_b</c> keeps the 62 random bits of a v4 Guid, so ids of two hosts never meet.
///     </para>
///     <para>
///         The order is what the queues' "oldest first" relies on, now that no identity column numbers the rows: within
///         a host strictly in the order the rows were made, across hosts by the millisecond. On SQL Server the key is
///         stored time-ordered too (<c>Rask:GuidOrder</c>), so the clustered index stays append-only.
///     </para>
/// </remarks>
internal static class OrderedGuid
{
    private static long s_lastTick;

    /// <summary>A new key, later than every key this process has made.</summary>
    internal static Guid New()
    {
        long last, tick;
        do
        {
            last = Volatile.Read(ref s_lastTick);
            tick = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() << 12, last + 1);
        }
        while (Interlocked.CompareExchange(ref s_lastTick, tick, last) != last);

        Span<byte> bytes = stackalloc byte[16];
        Guid.NewGuid().TryWriteBytes(bytes, bigEndian: true, out _);
        BinaryPrimitives.WriteInt64BigEndian(bytes, (tick >> 12 << 16) | 0x7000L | (tick & 0xFFFL));
        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>
    ///     The version-7 id of a row numbered <paramref name="number" /> that was made at <paramref name="at" />:
    ///     <c>xxxxxxxx-xxxx-7000-8nnn-nnnnnnnnnnnn</c>, the Unix millisecond, then the number in the 60 bits after the
    ///     version and the variant — the same id the migration that rebuilds an old integer key gives a row it keeps.
    /// </summary>
    /// <param name="at">When the row was made.</param>
    /// <param name="number">The row's number, from 0 to 2^60 - 1.</param>
    /// <remarks>Deterministic: the same row reads as the same id every time, and two rows apart in number never meet.</remarks>
    internal static Guid Of(DateTimeOffset at, long number)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(number);
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteInt64BigEndian(bytes, (at.ToUnixTimeMilliseconds() << 16) | 0x7000L);
        BinaryPrimitives.WriteInt64BigEndian(bytes[8..], (number & 0x0FFF_FFFF_FFFF_FFFFL) | unchecked((long)0x8000_0000_0000_0000UL));
        return new Guid(bytes, bigEndian: true);
    }
}
