using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.Data;

/// <summary>
/// Marks an <see cref="IMigrationsSqlGenerator"/> that emits the DDL enforcing
/// <see cref="RangeExclusionBuilderExtensions.HasNonOverlappingRange{TEntity}"/>.
/// </summary>
/// <remarks>
/// Implemented by each Rask provider package's migrations generator. It is internal on purpose: nobody but a
/// provider ever implements it, so it stays out of every app's completion list and reaches the providers
/// through <c>InternalsVisibleTo</c>, the way the SQLite packages already share internals.
/// </remarks>
internal interface IRangeExclusionEnforcer;

/// <summary>
/// A <see cref="RangeExclusionBuilderExtensions.HasNonOverlappingRange{TEntity}"/> rule the context's provider
/// would silently ignore, reported at boot.
/// </summary>
/// <remarks>
/// <para>
/// The rule is model metadata; a provider has to turn it into DDL. <c>UseRaskSqlite</c> does, and a plain
/// <c>UseSqlite</c>, <c>UseNpgsql</c> or <c>UseSqlServer</c> does not — so on those the app builds, migrates
/// and passes every test that does not deliberately collide two ranges, and then accepts the double booking
/// the rule was declared to stop. That is the failure this refuses to let ship.
/// </para>
/// <para>
/// THE MODEL AND THE SERVICES, NOT THE DATABASE: both are available without a connection, so this runs on an
/// app that has not migrated yet, and it fails only on a code mistake.
/// </para>
/// </remarks>
/// <typeparam name="TContext">The context <c>AddRaskData&lt;TContext&gt;</c> bound.</typeparam>
internal sealed class RangeExclusionCheck<TContext>(IServiceProvider services) : IHostedService
    where TContext : DbContext
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Resolved rather than injected: AddRaskData<TContext> has never required the factory up front — the
        // ambient binding resolves it lazily — so an app that registered its context with AddDbContext must
        // not start failing at boot with a DI error about a factory it never asked for. A scoped context is
        // just as good a place to read the model from, and with neither there is nothing to check.
        using var scope = services.CreateScope();
        using var owned = scope.ServiceProvider.GetService<IDbContextFactory<TContext>>()?.CreateDbContext();
        var db = owned ?? scope.ServiceProvider.GetService<TContext>();
        if (db is null)
        {
            return Task.CompletedTask;
        }

        var declaring = db.Model.GetEntityTypes()
            .Where(static e => e.FindAnnotation(RangeExclusionSpec.AnnotationName) is not null)
            .Select(static e => e.DisplayName())
            .Order(StringComparer.Ordinal)
            .ToList();

        if (declaring.Count == 0)
        {
            return Task.CompletedTask;
        }

        // Resolved without GetService<T>, which throws when the provider registers no migrations generator at
        // all (the in-memory provider) — that provider enforces nothing either, and deserves the same message.
        if (db.GetInfrastructure().GetService(typeof(IMigrationsSqlGenerator)) is IRangeExclusionEnforcer)
        {
            return Task.CompletedTask;
        }

        var subject = declaring.Count == 1
            ? $"{declaring[0]} declares"
            : $"{string.Join(", ", declaring)} declare";

        throw new InvalidOperationException(
            $"{subject} HasNonOverlappingRange, but {db.Database.ProviderName ?? "this provider"} does not "
            + "enforce it, so overlapping rows would be accepted without an error. Configure "
            + $"{typeof(TContext).Name} with UseRaskSqlite(services) from Rask.SQLite.EntityFrameworkCore, "
            + "whose migrations emit the constraint, or remove the rule.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
