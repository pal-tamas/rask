using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
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
/// Marks an <see cref="IMigrationsSqlGenerator"/> whose provider creates the index
/// <see cref="FullTextSearchBuilderExtensions.HasFullTextSearch{TEntity}"/> declares, and whose queries
/// translate <see cref="FullTextQueryableExtensions.Search{TEntity}"/>.
/// </summary>
/// <remarks>Internal for the same reason as <see cref="IRangeExclusionEnforcer"/>.</remarks>
internal interface IFullTextSearchEnforcer;

/// <summary>
/// Model metadata the context's provider would silently ignore — a
/// <see cref="RangeExclusionBuilderExtensions.HasNonOverlappingRange{TEntity}"/> rule or a
/// <see cref="FullTextSearchBuilderExtensions.HasFullTextSearch{TEntity}"/> index — reported at boot.
/// </summary>
/// <remarks>
/// <para>
/// Both are model metadata a provider has to turn into DDL. <c>UseRaskSqlite</c> does, and a plain
/// <c>UseSqlite</c>, <c>UseNpgsql</c> or <c>UseSqlServer</c> does not — so on those the app builds, migrates
/// and passes every test that does not deliberately exercise the feature, and then accepts the double booking
/// the rule was declared to stop, or fails on the first search. That is the failure this refuses to let ship.
/// </para>
/// <para>
/// THE MODEL AND THE SERVICES, NOT THE DATABASE: both are available without a connection, so this runs on an
/// app that has not migrated yet, and it fails only on a code mistake.
/// </para>
/// </remarks>
/// <typeparam name="TContext">The context <c>AddRaskData&lt;TContext&gt;</c> bound.</typeparam>
internal sealed class ProviderFeatureCheck<TContext>(IServiceProvider services) : IHostedService
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

        // Resolved without GetService<T>, which throws when the provider registers no migrations generator at
        // all (the in-memory provider) — that provider enforces nothing either, and deserves the same message.
        var generator = db.GetInfrastructure().GetService(typeof(IMigrationsSqlGenerator));
        var provider = db.Database.ProviderName ?? "this provider";

        if (generator is not IRangeExclusionEnforcer && Declaring(db.Model, RangeExclusionSpec.AnnotationName) is { Count: > 0 } ranges)
        {
            throw new InvalidOperationException(
                $"{Subject(ranges)} HasNonOverlappingRange, but {provider} does not enforce it, so overlapping rows "
                + $"would be accepted without an error. Configure {typeof(TContext).Name} with "
                + "UseRaskSqlite(services) from Rask.SQLite.EntityFrameworkCore, whose migrations emit the "
                + "constraint, or remove the rule.");
        }

        if (generator is not IFullTextSearchEnforcer && Declaring(db.Model, FullTextSearchSpec.AnnotationName) is { Count: > 0 } searches)
        {
            throw new InvalidOperationException(
                $"{Subject(searches)} HasFullTextSearch, but {provider} does not support it, so every Search(text) "
                + "would fail. Full-text search is SQLite-only for now: configure "
                + $"{typeof(TContext).Name} with UseRaskSqlite(services) from Rask.SQLite.EntityFrameworkCore, "
                + "or remove the declaration.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static List<string> Declaring(IModel model, string annotation) =>
        model.GetEntityTypes()
            .Where(e => e.FindAnnotation(annotation) is not null)
            .Select(static e => e.DisplayName())
            .Order(StringComparer.Ordinal)
            .ToList();

    private static string Subject(List<string> declaring) =>
        declaring.Count == 1
            ? $"{declaring[0]} declares"
            : $"{string.Join(", ", declaring)} declare";
}
