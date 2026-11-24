using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Sqlite.Infrastructure.Internal;
using Rask.Data;

namespace Rask.SQLite;

// SqliteModelValidator is EF Core's "internal" API, and the one place a provider's model is validated. The SQLite and
// provider suites map an optional value object whose members are all optional, so an upgrade that moves it goes red.
#pragma warning disable EF1001

/// <summary>
///     EF Core's SQLite model validator, which lets an optional value object whose members are all optional be held in
///     its row's columns (<see cref="OptionalComplexValues" />).
/// </summary>
/// <remarks>Registered by <see cref="FullTextSearchOptionsExtension" />, which every <c>UseRaskSqlite</c> context carries.</remarks>
internal sealed class RaskSqliteModelValidator(
    ModelValidatorDependencies dependencies, RelationalModelValidatorDependencies relationalDependencies)
    : SqliteModelValidator(dependencies, relationalDependencies)
{
    protected override void ValidatePropertyMapping(
        IComplexProperty complexProperty, IDiagnosticsLogger<DbLoggerCategory.Model.Validation> logger) =>
        OptionalComplexValues.Validate(complexProperty, () => base.ValidatePropertyMapping(complexProperty, logger));
}
#pragma warning restore EF1001
