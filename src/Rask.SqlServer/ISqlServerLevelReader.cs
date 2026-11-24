namespace Rask.SqlServer;

/// <summary>Asks a SQL Server which compatibility level EF Core should generate SQL for.</summary>
/// <remarks>A seam so the cache in front of it is tested without a server; the real one is <see cref="SqlServerLevelReader"/>.</remarks>
internal interface ISqlServerLevelReader
{
    /// <summary>
    /// The compatibility level of the database <paramref name="connectionString"/> names, or <see langword="null"/> when
    /// the server reports nothing Rask can map. Throws when the server cannot be reached.
    /// </summary>
    int? Read(string connectionString);
}
