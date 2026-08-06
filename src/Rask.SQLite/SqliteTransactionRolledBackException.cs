namespace Rask.SQLite;

/// <summary>
/// Raised inside the package when a statement that requires an open transaction is answered with a
/// contended lock and finds the transaction gone — SQLite's documented automatic rollback. It never
/// escapes <see cref="SqliteConnectionExtensions.ExecuteInImmediateTransactionAsync{T}"/>, which either
/// re-runs the whole transaction or converts this into a diagnosable <c>SqliteException</c>.
/// </summary>
internal sealed class SqliteTransactionRolledBackException(string sql, int attempt)
    : Exception($"'{sql}' was answered with a contended lock and found no transaction left, on attempt {attempt}.")
{
    /// <summary>The statement that lost its transaction — always <c>COMMIT;</c> in production.</summary>
    public string Sql { get; } = sql;

    /// <summary>Which pass of the busy-retry loop discovered it, 1-based.</summary>
    public int Attempt { get; } = attempt;
}
