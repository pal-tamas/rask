using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

/// <summary>Writes the <see cref="TenantReference" />s <c>TenantReferences&lt;T&gt;()</c> asks for onto a model being finalized.</summary>
internal static class TenantReferenceDeclaration
{
    /// <summary>
    ///     Declares the reference on every <see cref="PerTenant" /> table of the model that keeps a <c>TenantId</c> column
    ///     and has none yet, when <c>TenantReferences&lt;T&gt;()</c> asked for them; then names every one the model has by
    ///     <paramref name="name" />, the app's <c>ConstraintNames</c> rule.
    /// </summary>
    /// <param name="modelBuilder">The model being finalized.</param>
    /// <param name="name">The app's rule for a foreign key's name, or null to keep the name each has.</param>
    /// <exception cref="InvalidOperationException">The tenant is not mapped to a table with a key of one column.</exception>
    internal static void Declare(IConventionModelBuilder modelBuilder, Func<Constraint, string?>? name)
    {
        var model = modelBuilder.Metadata;
        var limit = model.GetMaxIdentifierLength();

        if (model.FindAnnotation(TenantReference.TenantAnnotation)?.Value is string tenantName)
        {
            var principal = Principal(model, tenantName);
            foreach (var entityType in model.GetEntityTypes().Where(e => KeepsTheTenant(e, tenantName)))
            {
                if (entityType.FindAnnotation(TenantReference.Annotation) is null && Column(entityType) is { } column)
                {
                    var table = entityType.GetTableName()!;
                    var declared = principal with { Name = Cut($"FK_{table}_{principal.PrincipalTable}_{column}", limit) };
                    entityType.Builder.HasAnnotation(TenantReference.Annotation, declared.ToString());
                }
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
                && name(new Constraint(ConstraintKind.ForeignKey, entityType.GetTableName()!, [column])) is { Length: > 0 } named)
            {
                entityType.Builder.HasAnnotation(TenantReference.Annotation, (declared with { Name = Cut(named, limit) }).ToString());
            }
        }
    }

    private static TenantReference Principal(IConventionModel model, string tenantName)
    {
        var tenant = model.GetEntityTypes().FirstOrDefault(e => IsTheTenant(e, tenantName));
        var table = tenant?.GetTableName();
        if (tenant is null || table is null || tenant.FindPrimaryKey()?.Properties is not [var key])
        {
            throw new InvalidOperationException(
                $"c.TenantReferences<{tenantName}>() points every PerTenant table at the tenant's key, and {tenantName} is "
                + "not an entity of this model mapped to a table with a key of one column. Name the app's tenant aggregate — "
                + "the one that says ITenant.");
        }

        var column = key.GetColumnName(StoreObjectIdentifier.Table(table, tenant.GetSchema())) ?? key.GetColumnName();
        return new TenantReference("", tenant.GetSchema(), table, column);
    }

    // A table of the tenant's rows: PerTenant, mapped to a table of its own, and not the tenant itself.
    private static bool KeepsTheTenant(IConventionEntityType entityType, string tenantName) =>
        !IsTheTenant(entityType, tenantName)
        && entityType.BaseType is null
        && !entityType.IsOwned()
        && entityType.GetTableName() is not null
        && ConventionRegistry.IsPerTenant(entityType.ClrType);

    private static bool IsTheTenant(IConventionEntityType entityType, string tenantName) =>
        !entityType.HasSharedClrType && string.Equals(entityType.ClrType.FullName, tenantName, StringComparison.Ordinal);

    private static string? Column(IConventionEntityType entityType) =>
        entityType.GetTableName() is { } table && entityType.FindProperty(Columns.TenantId) is { } property
            ? property.GetColumnName(StoreObjectIdentifier.Table(table, entityType.GetSchema())) ?? property.GetColumnName()
            : null;

    private static string Cut(string name, int? limit) => limit is { } max && name.Length > max ? name[..max] : name;
}
