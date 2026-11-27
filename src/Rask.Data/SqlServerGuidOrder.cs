using System.Globalization;
using System.Text;

namespace Rask.Data;

/// <summary>
///     The form a <see cref="Guid" /> is stored in on SQL Server, so a <c>uniqueidentifier</c> sorts as .NET, PostgreSQL and
///     SQLite sort the id — a version-7 id by the time it was made.
/// </summary>
/// <remarks>
///     <para>
///         SQL Server compares the sixteen bytes of a <c>uniqueidentifier</c> (as <see cref="Guid.ToByteArray()" /> lays
///         them out) in the order 10–15, 8, 9, 6, 7, 4, 5, 0–3; everyone else compares the id's own bytes, as its text
///         reads, first to last. The stored form puts the id's byte <c>i</c> where SQL Server looks <c>i</c>-th, so the
///         two orders are one. It is a permutation of the bytes: nothing is lost, and every id has exactly one stored form.
///     </para>
///     <para>
///         Raw SQL — <c>FromSql</c>, Dapper, SSMS — sees the stored form. <see cref="StoredFromText" /> writes the stored
///         form of an id's text in T-SQL, and <see cref="StoredFromNative" /> / <see cref="NativeFromStored" /> rewrite a
///         column from one layout to the other, which is what the one-time migration does.
///     </para>
/// </remarks>
internal static class SqlServerGuidOrder
{
    /// <summary>The model annotation saying the model's <c>uniqueidentifier</c> columns hold the stored form.</summary>
    internal const string Annotation = "Rask:GuidOrder";

    /// <summary>The annotation's value: ordered by the id's own bytes, so a version-7 id by time.</summary>
    internal const string ByTime = "Time";

    // Where SQL Server looks i-th, in Guid.ToByteArray()'s layout: the id's big-endian byte i is put there.
    private static ReadOnlySpan<byte> Order => [10, 11, 12, 13, 14, 15, 8, 9, 6, 7, 4, 5, 0, 1, 2, 3];

    // The stored layout's byte j comes from the NATIVE layout's byte StoredFromNativeBytes[j] (both as
    // Guid.ToByteArray() writes them, which is what CAST(… AS binary(16)) reads).
    internal static ReadOnlySpan<byte> StoredFromNativeBytes => [12, 13, 14, 15, 10, 11, 8, 9, 7, 6, 3, 2, 1, 0, 5, 4];

    // The native layout's byte j comes from the stored layout's byte NativeFromStoredBytes[j].
    internal static ReadOnlySpan<byte> NativeFromStoredBytes => [13, 12, 11, 10, 15, 14, 9, 8, 6, 7, 4, 5, 0, 1, 2, 3];

    // The stored layout's byte j comes from the id's big-endian (text-order) byte StoredFromTextBytes[j].
    internal static ReadOnlySpan<byte> StoredFromTextBytes => [12, 13, 14, 15, 10, 11, 8, 9, 6, 7, 0, 1, 2, 3, 4, 5];

    /// <summary>The form <paramref name="id" /> is stored in.</summary>
    internal static Guid ToStore(Guid id)
    {
        Span<byte> ordered = stackalloc byte[16];
        id.TryWriteBytes(ordered, bigEndian: true, out _);
        Span<byte> stored = stackalloc byte[16];
        for (var i = 0; i < 16; i++)
        {
            stored[Order[i]] = ordered[i];
        }

        return new Guid(stored);
    }

    /// <summary>The id <paramref name="stored" /> is the stored form of.</summary>
    internal static Guid FromStore(Guid stored)
    {
        Span<byte> bytes = stackalloc byte[16];
        stored.TryWriteBytes(bytes);
        Span<byte> ordered = stackalloc byte[16];
        for (var i = 0; i < 16; i++)
        {
            ordered[i] = bytes[Order[i]];
        }

        return new Guid(ordered, bigEndian: true);
    }

    /// <summary>
    ///     The T-SQL that turns an existing <c>uniqueidentifier</c> column (or expression), holding ids as SQL Server
    ///     stores them natively, into their stored form.
    /// </summary>
    /// <param name="column">The column, already delimited — <c>[Id]</c>.</param>
    internal static string StoredFromNative(string column) => Rearranged($"CAST({column} AS binary(16))", StoredFromNativeBytes);

    /// <summary>The T-SQL that turns a column of stored forms back into the ids as SQL Server stores them natively.</summary>
    /// <param name="column">The column, already delimited — <c>[Id]</c>.</param>
    internal static string NativeFromStored(string column) => Rearranged($"CAST({column} AS binary(16))", NativeFromStoredBytes);

    /// <summary>
    ///     The T-SQL for the stored form of the id an <c>nvarchar</c>/<c>varchar</c> expression spells
    ///     (<c>'0190a6b2-…'</c>, either case).
    /// </summary>
    /// <param name="text">The text expression — a variable, a literal, a column.</param>
    internal static string StoredFromText(string text) =>
        Rearranged($"CONVERT(binary(16), REPLACE({text}, '-', ''), 2)", StoredFromTextBytes);

    // CAST(SUBSTRING(b, 13, 4) + SUBSTRING(b, 11, 2) + … AS uniqueidentifier): each run of source bytes that stays in
    // order is one SUBSTRING.
    private static string Rearranged(string binary, ReadOnlySpan<byte> map)
    {
        var sql = new StringBuilder("CAST(");
        var j = 0;
        while (j < map.Length)
        {
            var run = 1;
            while (j + run < map.Length && map[j + run] == map[j] + run)
            {
                run++;
            }

            if (j > 0)
            {
                sql.Append(" + ");
            }

            sql.Append(CultureInfo.InvariantCulture, $"SUBSTRING({binary}, {map[j] + 1}, {run})");
            j += run;
        }

        return sql.Append(" AS uniqueidentifier)").ToString();
    }
}
