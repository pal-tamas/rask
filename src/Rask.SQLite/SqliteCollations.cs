using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Rask.SQLite;

/// <summary>
/// Registers the collating sequences a Rask SQLite connection needs. Like <see cref="SqlitePragmas"/>,
/// this is the single source of truth shared by the raw-ADO factory
/// (<see cref="IRaskSqliteConnectionFactory"/>) and the Entity Framework Core interceptor in the
/// <c>Rask.SQLite.EntityFrameworkCore</c> package.
/// </summary>
/// <remarks>
/// <para>
/// SQLite has no native decimal type, so EF Core stores a <see cref="decimal"/> as <c>TEXT</c> in a
/// culture-invariant fixed-point format (<c>19.95</c>, never <c>19,95</c>) and emits
/// <c>ORDER BY "x" COLLATE EF_DECIMAL</c> so the text sorts numerically rather than lexicographically.
/// EF registers that collation itself — but as
/// <c>decimal.Compare(decimal.Parse(x), decimal.Parse(y))</c>, with <b>no <see cref="IFormatProvider"/></b>,
/// so it parses the invariant text using <see cref="CultureInfo.CurrentCulture"/>. The result depends on
/// the machine's locale:
/// </para>
/// <list type="bullet">
/// <item><description>
/// Where <c>.</c> is the <i>group</i> separator (de-DE, fr-FR, …), <c>"19.95"</c> parses as <c>1995</c> and
/// rows come back <b>silently mis-ordered</b>.
/// </description></item>
/// <item><description>
/// Where <c>.</c> is neither separator (en-HU, hu-HU, …), the parse <b>throws</b>. The exception is raised
/// inside a native SQLite callback, cannot be unwound across that boundary, and so <b>terminates the
/// process</b> rather than surfacing as a catchable query error.
/// </description></item>
/// <item><description>
/// Independently of locale, SQLite's dynamic typing lets any text land in a <c>decimal</c> column (a
/// direct <c>INSERT</c>, an external tool, a legacy row). EF's collation throws on it — the same fatal
/// crash.
/// </description></item>
/// </list>
/// <para>
/// <see cref="Apply"/> re-registers <c>EF_DECIMAL</c> under the same name with an invariant, total,
/// non-throwing comparison, which is what EF's own generated SQL then uses. Nothing in the database file
/// changes: no column type, no collation in the DDL, no migration, and every other tool still reads the
/// file exactly as before.
/// </para>
/// <para>
/// <b>Do not delete this when EF Core is upgraded.</b> The culture half is fixed upstream in EF Core
/// 11.0.0 preview 1 (<a href="https://github.com/dotnet/efcore/issues/37432">dotnet/efcore#37432</a>),
/// which adds <see cref="CultureInfo.InvariantCulture"/> to the parse — but the fix still uses
/// <c>decimal.Parse</c>, not <c>TryParse</c>, so on EF 11 the collation continues to <b>throw, and take
/// the process with it</b>, the moment a value that is not a number reaches the column. EF 11 fixes the
/// first two bullets above and leaves the third, which is tracked upstream as
/// <a href="https://github.com/dotnet/efcore/issues/38870">dotnet/efcore#38870</a>; until that closes,
/// this registration is the only one that is total. Re-check
/// <c>SqliteRelationalConnection.InitializeDbConnection</c> before removing it.
/// </para>
/// <para>
/// It must run on <b>every</b> connection open, not once: Microsoft.Data.Sqlite pools connections and
/// <c>Deactivate()</c> un-registers functions and collations when one is returned to the pool.
/// </para>
/// </remarks>
public static class SqliteCollations
{
    /// <summary>
    /// The name of the decimal collating sequence. This is deliberately EF Core's own name — EF emits
    /// <c>COLLATE EF_DECIMAL</c> into the SQL it generates for a <see cref="decimal"/> ordering, so
    /// replacing the implementation under that name is what makes those queries correct. Naming a
    /// column or index <c>COLLATE EF_DECIMAL</c> explicitly is also supported, and picks up the same
    /// implementation.
    /// </summary>
    public const string Decimal = "EF_DECIMAL";

    /// <summary>
    /// Registers Rask's collating sequences on an open <paramref name="connection"/>, replacing any
    /// same-named sequence already registered on it. Call on every open.
    /// </summary>
    /// <param name="connection">An open SQLite connection.</param>
    public static void Apply(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        connection.CreateCollation(Decimal, static (x, y) => CompareDecimal(x, y));
    }

    /// <summary>
    /// Orders two values from a <see cref="decimal"/> column numerically, reading both with
    /// <see cref="CultureInfo.InvariantCulture"/> to match the format EF Core writes.
    /// </summary>
    /// <remarks>
    /// Total and non-throwing by construction. A collation runs inside SQLite's native comparison loop,
    /// where a managed exception cannot be unwound and takes the process down with it, so anything that
    /// does not parse is ordered rather than rejected: non-numeric text sorts after every number, and
    /// ties among such values fall back to an ordinal comparison so the order stays deterministic (SQLite
    /// requires a consistent collation, and an inconsistent one corrupts index lookups).
    /// </remarks>
    internal static int CompareDecimal(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        // SQLite never passes NULL to a collation (NULLs order by the query's own rules), but the
        // delegate's signature allows it and a total order costs one comparison.
        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var xIsNumber = decimal.TryParse(x, NumberStyles.Number, CultureInfo.InvariantCulture, out var xValue);
        var yIsNumber = decimal.TryParse(y, NumberStyles.Number, CultureInfo.InvariantCulture, out var yValue);

        if (xIsNumber && yIsNumber)
        {
            return decimal.Compare(xValue, yValue);
        }

        if (xIsNumber)
        {
            return -1;
        }

        if (yIsNumber)
        {
            return 1;
        }

        return string.CompareOrdinal(x, y);
    }
}
