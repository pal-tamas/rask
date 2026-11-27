using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Generators.Shared;

namespace Rask.Data;

/// <summary>Writes the <see cref="TenantReference" />s of the app's tenant onto a model being finalized.</summary>
internal static class TenantReferenceDeclaration
{
    /// <summary>
    ///     Declares the reference on every table of the model that <see cref="IBelongsTo{TTenant}" /> the app's tenant,
    ///     keeps a <c>TenantId</c> column and has none yet, when the model maps that tenant; then names every one the model has by
    ///     <paramref name="name" />, the app's <c>ConstraintNames</c> rule. A read model is left as it is: the reference is
    ///     the schema's, which only the write model makes, and a read model maps the tenant's read face, not the tenant.
    /// </summary>
    /// <param name="modelBuilder">The model being finalized.</param>
    /// <param name="name">The app's rule for a foreign key's name, or null to keep the name each has.</param>
    internal static void Declare(IConventionModelBuilder modelBuilder, Func<Constraint, string?>? name)
    {
        var model = modelBuilder.Metadata;
        if (model.FindAnnotation(ReadModelRegistry.Annotation) is not null)
        {
            return;
        }

        var limit = model.GetMaxIdentifierLength();

        foreach (var entityType in model.GetEntityTypes().Where(static e => e.FindAnnotation(TenantReference.Annotation) is null))
        {
            if (TheTenantOf(entityType) is { } tenant &&
                Principal(model, tenant) is { } principal &&
                KeepsTheTenant(entityType, tenant) &&
                Column(entityType) is { } column)
            {
                var table = entityType.GetTableName()!;
                var declared = principal with { Name = Cut($"FK_{table}_{principal.PrincipalTable}_{column}", limit) };
                entityType.Builder.HasAnnotation(TenantReference.Annotation, declared.ToString());
            }
        }

        if (name is null)
        {
            return;
        }

        foreach (var entityType in model.GetEntityTypes())
        {
            if (TenantReference.Parse(entityType.FindAnnotation(TenantReference.Annotation)?.Value as string) is { } declared
                && Column(entityType) is { } column
                && name(new Constraint(ConstraintKind.ForeignKey, entityType.GetTableName()!, [column], declared.Name)) is { Length: > 0 } named)
            {
                entityType.Builder.HasAnnotation(TenantReference.Annotation, (declared with { Name = Cut(named, limit) }).ToString());
            }
        }
    }

    // The tenant the table belongs to, when it is the app's own — the aggregate that says ITenant, as the generated code
    // registered it. A test assembly's tenants of every key type are registered by none, and their tables refer to nothing.
    private static Type? TheTenantOf(IConventionEntityType entityType) =>
        ConventionRegistry.TenantOf(entityType.ClrType) is { } tenant && tenant == CurrentRows.TenantAggregate ? tenant : null;

    // The tenant's table and key column, or null when this model does not map the tenant — a context of its own.
    private static TenantReference? Principal(IConventionModel model, Type tenantType)
    {
        var tenant = model.FindEntityType(tenantType);
        var table = tenant?.GetTableName();
        if (tenant is null || table is null || tenant.FindPrimaryKey()?.Properties is not [var key])
        {
            return null;
        }

        var column = key.GetColumnName(StoreObjectIdentifier.Table(table, tenant.GetSchema())) ?? key.GetColumnName();
        return new TenantReference("", tenant.GetSchema(), table, column);
    }

    // A table of the tenant's rows: mapped to a table of its own, and not the tenant itself. Partitioned, or the one
    // that says IBelongsTo<Tenant> without being partitioned — Rask.Auth's accounts, whose tenant is optional.
    private static bool KeepsTheTenant(IConventionEntityType entityType, Type tenant) =>
        entityType.ClrType != tenant
        && entityType.BaseType is null
        && !entityType.IsOwned()
        && entityType.GetTableName() is not null;

    private static string? Column(IConventionEntityType entityType) =>
        entityType.GetTableName() is { } table && entityType.FindProperty(Columns.TenantId) is { } property
            ? property.GetColumnName(StoreObjectIdentifier.Table(table, entityType.GetSchema())) ?? property.GetColumnName()
            : null;

    private static string Cut(string name, int? limit) => limit is { } max ? StoreNames.Fit(name, max) : name;
}
