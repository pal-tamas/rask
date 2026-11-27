using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Rask;

/// <summary>
///     A new key: a version-7 <see cref="Guid" />, made by Rask before the insert, time-ordered and strictly increasing
///     — <c>Uuid.Create()</c>. What a typed id's generated <c>Create()</c> returns, and the key of every row the
///     batteries write, so a plain <see cref="Guid" /> key is made the same way.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Guid.CreateVersion7()" /> fills <c>rand_a</c> at random, so two ids made in one millisecond sort
///         either way. Here the 48-bit Unix millisecond and a 12-bit counter in <c>rand_a</c> are one 60-bit tick
///         (RFC 9562 §6.2, method 1), advanced by a compare-and-swap: a new millisecond restarts the counter, a busy one
///         adds 1, and a full one (4096 ids) or a clock that stepped back carries into the next millisecond, so the
///         stamp runs ahead rather than repeating or going back. <c>rand_b</c> keeps the 62 random bits of a v4 Guid, so
///         the ids of two hosts never meet.
///     </para>
///     <para>
///         The order is the one every database Rask runs on sorts the key by: PostgreSQL's <c>uuid</c> and SQLite's
///         text as they are, SQL Server's <c>uniqueidentifier</c> because Rask stores it time-ordered
///         (<c>Rask:GuidOrder</c>) — so the key's index stays append-only and "oldest first" is the key's order.
///     </para>
/// </remarks>
public static class Uuid
{
    // One sequence per time source. Rask's own clock — what Create() reads and every battery is handed as its
    // TimeProvider — is one sequence for the whole process; a test's own TimeProvider counts on its own, so its ids
    // read its time and nothing another test made can run them ahead.
    private static readonly Sequence AppClock = new();
    private static readonly ConditionalWeakTable<TimeProvider, Sequence> Others = new();

    /// <summary>A new id, later than every one this process made: stamped with <see cref="Clock.Now" />.</summary>
    /// <returns>A version-7 <see cref="Guid" />.</returns>
    public static Guid Create() => AppClock.Next(Clock.Now);

    /// <summary>
    ///     A new id stamped with <paramref name="time" />'s now, later than every one made on the same time source — for a
    ///     service handed a <see cref="TimeProvider" />, so a test's fake time stamps its ids.
    /// </summary>
    /// <param name="time">The time source; Rask's own (<c>Clock.TimeProvider</c>) is the sequence <see cref="Create()" /> runs.</param>
    /// <returns>A version-7 <see cref="Guid" />.</returns>
    public static Guid Create(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        var sequence = ReferenceEquals(time, Clock.TimeProvider)
            ? AppClock
            : Others.GetValue(time, static _ => new Sequence());
        return sequence.Next(time.GetUtcNow());
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

    private sealed class Sequence
    {
        private long _lastTick;

        internal Guid Next(DateTimeOffset now)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(now, DateTimeOffset.UnixEpoch);
            var millisecond = now.ToUnixTimeMilliseconds();

            long last, tick;
            do
            {
                last = Volatile.Read(ref _lastTick);
                tick = Math.Max(millisecond << 12, last + 1);
            }
            while (Interlocked.CompareExchange(ref _lastTick, tick, last) != last);

            Span<byte> bytes = stackalloc byte[16];
            Guid.NewGuid().TryWriteBytes(bytes, bigEndian: true, out _);
            BinaryPrimitives.WriteInt64BigEndian(bytes, (tick >> 12 << 16) | 0x7000L | (tick & 0xFFFL));
            return new Guid(bytes, bigEndian: true);
        }
    }
}
