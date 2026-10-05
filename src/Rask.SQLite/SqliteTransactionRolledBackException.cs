namespace Rask.SQLite;

/// <summary>
/// Raised inside the package when a statement that requires an open transaction is answered with a
/// contended lock and finds the transaction gone — SQLite's documented automatic rollback. It never
/// escapes <see cref="SqliteConnectionExtensions.InImmediateTransaction{T}"/>, which either
/// re-runs the whole transaction or converts this into a diagnosable <c>SqliteException</c>.
/// </summary>
public sealed class SqliteTransactionRolledBackException : Exception
{
    /// <summary>Creates the exception with a default message.</summary>
    public SqliteTransactionRolledBackException()
        : this("A contended statement found its transaction rolled back.")
    {
    }

    /// <summary>Creates the exception with <paramref name="message"/>.</summary>
    public SqliteTransactionRolledBackException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with <paramref name="message"/> and the exception that caused it.</summary>
    public SqliteTransactionRolledBackException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception for <paramref name="sql"/>, lost on pass <paramref name="attempt"/>.</summary>
    public SqliteTransactionRolledBackException(string sql, int attempt)
        : base($"'{sql}' was answered with a contended lock and found no transaction left, on attempt {attempt}.")
    {
        Sql = sql;
        Attempt = attempt;
    }

    /// <summary>The statement that lost its transaction — always <c>COMMIT;</c> in production.</summary>
    public string Sql { get; } = "";

    /// <summary>Which pass of the busy-retry loop discovered it, 1-based; <c>0</c> when not known.</summary>
    public int Attempt { get; }
}
