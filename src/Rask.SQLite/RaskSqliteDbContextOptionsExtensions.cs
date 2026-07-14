using Microsoft.EntityFrameworkCore;

namespace Rask.SQLite;

/// <summary>
/// Entity Framework Core entry point: a drop-in replacement for <c>UseSqlite</c> that also wires the
/// Rails-style production pragmas onto every connection the context opens.
/// </summary>
public static class RaskSqliteDbContextOptionsExtensions
{
    /// <summary>
    /// Configures the context to use SQLite with <paramref name="connectionString"/> and applies the
    /// <see cref="SqlitePragmaOptions"/> (Rails production defaults, overridable via
    /// <paramref name="configure"/>) on every connection open. Swap your <c>UseSqlite(cs)</c> for
    /// <c>UseRaskSqlite(cs)</c> and you are done.
    /// </summary>
    public static DbContextOptionsBuilder UseRaskSqlite(
        this DbContextOptionsBuilder optionsBuilder,
        string connectionString,
        Action<SqlitePragmaOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        var options = new SqlitePragmaOptions();
        configure?.Invoke(options);
        options.Validate();

        return optionsBuilder
            .UseSqlite(connectionString)
            .AddInterceptors(new RaskSqliteConnectionInterceptor(options));
    }

    /// <summary>
    /// The strongly-typed overload of
    /// <see cref="UseRaskSqlite(DbContextOptionsBuilder, string, Action{SqlitePragmaOptions}?)"/>, so
    /// <c>new DbContextOptionsBuilder&lt;TContext&gt;().UseRaskSqlite(cs).Options</c> keeps its
    /// <see cref="DbContextOptions{TContext}"/> type.
    /// </summary>
    public static DbContextOptionsBuilder<TContext> UseRaskSqlite<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        string connectionString,
        Action<SqlitePragmaOptions>? configure = null)
        where TContext : DbContext
    {
        UseRaskSqlite((DbContextOptionsBuilder)optionsBuilder, connectionString, configure);
        return optionsBuilder;
    }
}
