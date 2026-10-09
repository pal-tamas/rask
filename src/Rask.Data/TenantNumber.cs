using System.Buffers.Binary;

namespace Rask.Data;

/// <summary>
///     How a tenant an app numbers — <c>42</c> — is carried as the <see cref="Guid" /> the framework speaks.
/// </summary>
/// <remarks>
///     <para>
///         Fixed, and part of the contract: the first eight bytes are zero and the last eight are the number,
///         big-endian. Tenant <c>42</c> is <c>00000000-0000-0000-0000-00000000002a</c>, so the number can be
///         read off a job row, a cache key or a log line by eye.
///     </para>
///     <para>
///         No identifier a library generates lands there: every RFC 9562 version sets a non-zero version
///         nibble in the first eight bytes, so a real <see cref="Guid" /> is never mistaken for a number, and
///         a number never reads back as anything but itself.
///     </para>
///     <para>
///         Tenant <c>0</c> is <see cref="Guid.Empty" />, which the batteries read as "no tenant". Number
///         tenants from one.
///     </para>
/// </remarks>
internal static class TenantNumber
{
    private const int GuidSize = 16;
    private const int NumberOffset = 8;

    /// <summary>The <see cref="Guid" /> that carries <paramref name="number" />.</summary>
    internal static Guid ToGuid(long number)
    {
        Span<byte> bytes = stackalloc byte[GuidSize];
        bytes.Clear();
        BinaryPrimitives.WriteInt64BigEndian(bytes[NumberOffset..], number);

        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>Reads the number <paramref name="tenant" /> carries, when it carries one.</summary>
    /// <returns><see langword="false" /> for a tenant that is an identifier in its own right.</returns>
    internal static bool TryRead(Guid tenant, out long number)
    {
        Span<byte> bytes = stackalloc byte[GuidSize];
        tenant.TryWriteBytes(bytes, bigEndian: true, out _);

        if (bytes[..NumberOffset].ContainsAnyExcept((byte)0))
        {
            number = 0;
            return false;
        }

        number = BinaryPrimitives.ReadInt64BigEndian(bytes[NumberOffset..]);
        return true;
    }
}
