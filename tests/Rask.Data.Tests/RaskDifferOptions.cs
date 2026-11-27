using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Rask.Data.Tests;

/// <summary>
///     The ONE options shape for a test that diffs models through Rask's differ, which UseRaskSqlite,
///     UseRaskSqlServer, UseRaskPostgres and a RaskApp register (their wiring is pinned in their own test projects).
/// </summary>
/// <remarks>
///     Each <c>ReplaceService</c> combination is one more EF Core service provider for the whole test process
///     (<see cref="ServiceProviderBudget" />), so every differ test shares this one: Rask's differ, and
///     <see cref="EachContextItsOwnModel" /> for the tests whose contexts share a type but declare different models.
/// </remarks>
internal static class RaskDifferOptions
{
    /// <summary>Puts Rask's differ in place, each context building its own model.</summary>
    /// <typeparam name="TBuilder">The builder, handed back as it came.</typeparam>
    /// <param name="builder">The options, laid out by <see cref="OptionsLayout.LikeAddDbContext{TContext}" />.</param>
    /// <returns>The same builder.</returns>
    public static TBuilder WithRaskDiffer<TBuilder>(this TBuilder builder)
        where TBuilder : DbContextOptionsBuilder
    {
        builder
            .ReplaceService<IMigrationsModelDiffer, RaskMigrationsModelDiffer>()
            .ReplaceService<IModelCacheKeyFactory, EachContextItsOwnModel>();
        return builder;
    }
}
