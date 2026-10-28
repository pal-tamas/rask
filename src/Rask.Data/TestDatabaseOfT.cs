using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     A <see cref="TestDatabase" /> over the app's own context, from
///     <see cref="TestDatabase.Start{TContext}(Action{DbContextOptionsBuilder}, TimeProvider?, CancellationToken)" />.
/// </summary>
/// <typeparam name="TContext">The app's context.</typeparam>
public sealed class TestDatabase<TContext> : TestDatabase
    where TContext : RaskDbContext
{
    internal TestDatabase(TContext schemaOwner, Func<RaskReadDbContext> openRead, Func<DbContext> openWrite)
        : base(schemaOwner, openRead, openWrite)
    {
        Context = schemaOwner;
    }

    /// <summary>The fixture's own context, as the app's type — the way to seed rows and to run a domain operation under test.</summary>
    public new TContext Context { get; }
}
