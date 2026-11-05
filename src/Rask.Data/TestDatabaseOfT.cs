using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     A <see cref="TestDatabase" /> over the app's own context, from
///     <see cref="TestDatabase.Start{TContext}(Action{DbContextOptionsBuilder}, TimeProvider?, CancellationToken)" />.
/// </summary>
/// <typeparam name="TContext">The app's context.</typeparam>
public sealed class TestDatabase<[DynamicallyAccessedMembers(DataTrimming.Context)] TContext> : TestDatabase
    where TContext : RaskDbContext
{
    internal TestDatabase(TContext schemaOwner, Func<RaskReadDbContext> openRead, Func<TContext> openWrite, IAsyncDisposable? lease)
        : base(schemaOwner, openRead, openWrite, lease)
    {
        Context = schemaOwner;
        Factory = new TestContextFactory<TContext>(openWrite);
    }

    /// <summary>The fixture's own context, as the app's type — the way to seed rows and to run a domain operation under test.</summary>
    public new TContext Context { get; }

    /// <summary>
    ///     Fresh contexts over the same database, configured as the fixture's own — what a handler, a service or a
    ///     job that takes an <see cref="IDbContextFactory{TContext}" /> is handed in a test.
    /// </summary>
    /// <remarks>Each <c>CreateDbContext()</c> is a new, unshared context that its caller disposes.</remarks>
    public IDbContextFactory<TContext> Factory { get; }
}
