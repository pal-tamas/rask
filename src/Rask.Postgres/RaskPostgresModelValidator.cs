using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;
using Rask.Data;

namespace Rask.Postgres;

// NpgsqlModelValidator is the provider's "internal" API, and the one place its model is validated. The provider suite
// maps an optional value object whose members are all optional, so an upgrade that moves it goes red.
#pragma warning disable EF1001

/// <summary>
///     Npgsql's model validator, which lets an optional value object whose members are all optional be held in its
///     row's columns (<see cref="OptionalComplexValues" />).
/// </summary>
/// <remarks>Registered by <see cref="PostgresFullTextSearchOptionsExtension" />, which every <c>UseRaskPostgres</c> context carries.</remarks>
internal sealed class RaskPostgresModelValidator(
    ModelValidatorDependencies dependencies,
    RelationalModelValidatorDependencies relationalDependencies,
    INpgsqlSingletonOptions npgsqlSingletonOptions)
    : NpgsqlModelValidator(dependencies, relationalDependencies, npgsqlSingletonOptions)
{
    protected override void ValidatePropertyMapping(
        IComplexProperty complexProperty, IDiagnosticsLogger<DbLoggerCategory.Model.Validation> logger) =>
        OptionalComplexValues.Validate(complexProperty, () => base.ValidatePropertyMapping(complexProperty, logger));
}
#pragma warning restore EF1001
