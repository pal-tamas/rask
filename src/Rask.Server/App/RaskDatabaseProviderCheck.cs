using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Rask;

/// <summary>
/// Fails the start when the app's own context opens a different database than <c>Rask:Database:Provider</c> names.
/// </summary>
/// <remarks>
/// <para>
/// <c>RaskApp</c> wires the batteries by that setting — where the log goes, whether snapshots run — while the app's own
/// <c>AddDbContextFactory</c> call decides where its data goes. A Program.cs still calling <c>UseRaskSqlite(sp)</c> after
/// the setting moved to <c>postgres</c> would split the two silently, so the disagreement is reported at start, naming
/// the call that keeps them together. Registered only for an app-owned context: <c>RaskAppDbContext</c> is wired with
/// <c>UseRaskDatabase</c> and cannot disagree.
/// </para>
/// <para>
/// An options type, validated on start, rather than a hosted service: options are validated before any hosted service
/// starts, in registration order, and this one is registered ahead of every battery — so a battery that fails on the
/// wrong database cannot report first and hide the mistake that caused it.
/// </para>
/// </remarks>
/// <typeparam name="TContext">The application context <c>RaskApp</c> wired the batteries against.</typeparam>
internal sealed class RaskDatabaseProviderCheck<TContext>
    where TContext : DbContext
{
    /// <summary>Throws when the context's provider is not the configured one; returns <c>true</c> otherwise.</summary>
#pragma warning disable CA1822, S2325 // the options instance is what Validate<TDep1, TDep2> hands the check
    internal bool Verify(IDbContextFactory<TContext> contexts, IConfiguration configuration)
#pragma warning restore CA1822, S2325
    {
        var expected = RaskDatabase.Provider(configuration);

        using var db = contexts.CreateDbContext();
        var actual = db.Database.ProviderName;
        if (string.Equals(actual, RaskDatabase.EfProviderName(expected), StringComparison.Ordinal))
        {
            return true;
        }

        throw new InvalidOperationException(
            $"{RaskDatabase.ProviderKey} is {RaskDatabase.Name(expected)}, but {typeof(TContext).Name} opens "
            + $"{actual ?? "no database provider"}, so the batteries would be wired for one database while the app talks to "
            + $"another. Register the context with AddDbContextFactory<{typeof(TContext).Name}>((sp, o) => "
            + "o.UseRaskDatabase(sp)) so the setting picks its provider.");
    }
}
