using Microsoft.EntityFrameworkCore.Migrations;

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
