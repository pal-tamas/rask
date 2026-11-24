using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data;

/// <summary>
///     The model validator of a <see cref="RaskDbContext" /> on a provider Rask does not configure — a plain
///     <c>UseSqlite</c>, <c>UseSqlServer</c> or <c>UseNpgsql</c>: the provider's own, and, for a model it refuses only for
///     an optional value object whose members are all optional (<see cref="OptionalComplexValues" />), EF Core's
///     relational checks without that one.
/// </summary>
/// <remarks>
///     <c>UseRaskSqlite</c>, <c>UseRaskSqlServer</c> and <c>UseRaskPostgres</c> register a validator of their provider
///     that never refuses such a property, so this one only ever falls back on a provider Rask does not configure — and
///     there a model holding such a property is checked as every relational database is, without the provider's own
///     extra checks, rather than refused.
/// </remarks>
/// <param name="provider">The provider's validator.</param>
/// <param name="relational">EF Core's relational checks, without the one refusal.</param>
internal sealed class OptionalComplexValueValidator(IModelValidator provider, Func<IModelValidator> relational) : IModelValidator
{
    /// <summary>
    ///     Wraps the validator the provider registered, from the <c>ApplyServices</c> of an options extension every
    ///     <see cref="RaskDbContext" /> carries — after the provider's own, as a context's <c>OnConfiguring</c> adds it.
    /// </summary>
    /// <param name="services">EF Core's internal service collection.</param>
    internal static void Register(IServiceCollection services)
    {
        if (services.LastOrDefault(static d => d.ServiceType == typeof(IModelValidator)) is not { ImplementationType: { } type })
        {
            return;
        }

        services.AddSingleton<IModelValidator>(sp => new OptionalComplexValueValidator(
            (IModelValidator)ActivatorUtilities.CreateInstance(sp, type),
            () => ActivatorUtilities.CreateInstance<Relational>(sp)));
    }

    public void Validate(IModel model, IDiagnosticsLogger<DbLoggerCategory.Model.Validation> logger)
    {
        try
        {
            provider.Validate(model, logger);
        }
        catch (InvalidOperationException refused) when (OptionalComplexValues.Refuses(model, refused))
        {
            relational().Validate(model, logger);
        }
    }

    // EF Core's relational validator, which lets an optional value object whose members are all optional pass.
    private sealed class Relational(
        ModelValidatorDependencies dependencies, RelationalModelValidatorDependencies relationalDependencies)
        : RelationalModelValidator(dependencies, relationalDependencies)
    {
        protected override void ValidatePropertyMapping(
            IComplexProperty complexProperty, IDiagnosticsLogger<DbLoggerCategory.Model.Validation> logger) =>
            OptionalComplexValues.Validate(complexProperty, () => base.ValidatePropertyMapping(complexProperty, logger));
    }
}
