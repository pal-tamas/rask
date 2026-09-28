using Microsoft.EntityFrameworkCore.Migrations;

namespace Rask.Data;

/// <summary>
/// Marks an <see cref="IMigrationsSqlGenerator"/> whose provider creates the index
/// <see cref="FullTextSearchBuilderExtensions.HasFullTextSearch{TEntity}"/> declares, and whose queries
/// translate <see cref="FullTextQueryableExtensions.Search{TEntity}"/>.
/// </summary>
/// <remarks>Internal for the same reason as <see cref="IRangeExclusionEnforcer"/>.</remarks>
internal interface IFullTextSearchEnforcer;
