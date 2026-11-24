using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal;
using Rask.Data;

namespace Rask.SqlServer;

// SqlServerModelValidator is EF Core's "internal" API, and the one place a provider's model is validated. The provider
// suite maps an optional value object whose members are all optional, so an upgrade that moves it goes red.
#pragma warning disable EF1001

/// <summary>
///     EF Core's SQL Server model validator, which lets an optional value object whose members are all optional be held
///     in its row's columns (<see cref="OptionalComplexValues" />).
/// </summary>
/// <remarks>Registered by <see cref="RaskSqlServerOptionsExtension" />, which every <c>UseRaskSqlServer</c> context carries.</remarks>
internal sealed class RaskSqlServerModelValidator(
    ModelValidatorDependencies dependencies, RelationalModelValidatorDependencies relationalDependencies)
    : SqlServerModelValidator(dependencies, relationalDependencies)
{
    protected override void ValidatePropertyMapping(
        IComplexProperty complexProperty, IDiagnosticsLogger<DbLoggerCategory.Model.Validation> logger) =>
        OptionalComplexValues.Validate(complexProperty, () => base.ValidatePropertyMapping(complexProperty, logger));
}
#pragma warning restore EF1001
