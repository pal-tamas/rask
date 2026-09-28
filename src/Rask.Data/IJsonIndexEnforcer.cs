using Microsoft.EntityFrameworkCore.Migrations;

namespace Rask.Data;

/// <summary>
/// Marks an <see cref="IMigrationsSqlGenerator"/> whose provider creates the expression indexes
/// <see cref="JsonIndexBuilderExtensions.HasJsonIndex{TEntity}"/> declares.
/// </summary>
/// <remarks>Internal for the same reason as <see cref="IRangeExclusionEnforcer"/>.</remarks>
internal interface IJsonIndexEnforcer;
