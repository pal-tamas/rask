using Microsoft.EntityFrameworkCore.Migrations;

namespace Rask.Data;

/// <summary>
///     Marks an <see cref="IMigrationsSqlGenerator" /> that writes the triggers enforcing
///     <see cref="NoOverlapBuilderExtensions.NoOverlap{TEntity}(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder{TEntity}, System.Linq.Expressions.Expression{Func{TEntity, object}}, System.Linq.Expressions.Expression{Func{TEntity, object}}, string)" />
///     itself — SQLite's, whose table rebuilds take every trigger with them. PostgreSQL and SQL Server get theirs from
///     <see cref="RaskMigrationsModelDiffer" />, as statements of the migration.
/// </summary>
/// <remarks>
///     Internal on purpose: nobody but a provider ever implements it, so it stays out of every app's completion list and
///     reaches the providers through <c>InternalsVisibleTo</c>.
/// </remarks>
internal interface INoOverlapEnforcer;
