using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     The members EF Core keeps on the types it reflects over, as <see cref="DynamicallyAccessedMembersAttribute" />
///     sets (#1132).
/// </summary>
/// <remarks>
///     EF Core declares these on <c>DbContext.Set&lt;TEntity&gt;</c>, <c>DbSet&lt;&gt;</c>, <c>ModelBuilder.Entity(Type)</c>,
///     <c>IModel.FindEntityType(Type)</c> and <c>IDbContextFactory&lt;TContext&gt;</c>, but keeps its own named sets
///     internal. A Rask path that hands a type on to one of them must carry AT LEAST the same set, or the trim analyzer
///     reports it — a build error here, since this assembly is <c>IsTrimmable</c> under warnings-as-errors. Mirrored
///     from EF Core 10's annotations; widen them if EF widens its own.
/// </remarks>
internal static class DataTrimming
{
    /// <summary>What EF keeps on an entity type: its constructors, fields and properties at any visibility, and its interfaces.</summary>
    public const DynamicallyAccessedMemberTypes Entity =
        DynamicallyAccessedMemberTypes.PublicConstructors
        | DynamicallyAccessedMemberTypes.NonPublicConstructors
        | DynamicallyAccessedMemberTypes.PublicFields
        | DynamicallyAccessedMemberTypes.NonPublicFields
        | DynamicallyAccessedMemberTypes.PublicProperties
        | DynamicallyAccessedMemberTypes.NonPublicProperties
        | DynamicallyAccessedMemberTypes.Interfaces;

    /// <summary>What EF keeps on a context type: its constructors and its public properties.</summary>
    public const DynamicallyAccessedMemberTypes Context =
        DynamicallyAccessedMemberTypes.PublicConstructors
        | DynamicallyAccessedMemberTypes.NonPublicConstructors
        | DynamicallyAccessedMemberTypes.PublicProperties;

    /// <summary>
    ///     EF Core's own <c>[RequiresUnreferencedCode]</c> message on the <c>DbContext</c> constructor, repeated on
    ///     Rask's contexts so an app's context gets exactly the IL2026 a plain <c>DbContext</c> subclass gets.
    /// </summary>
    public const string EfCoreUnreferencedCode =
        "EF Core isn't fully compatible with trimming, and running the application may generate unexpected runtime "
        + "failures. Some specific coding pattern are usually required to make trimming work properly, see "
        + "https://aka.ms/efcore-docs-trimming for more details.";

    /// <summary>
    ///     The entity type <paramref name="clrType" /> is mapped as, found by walking the model rather than through
    ///     <c>IModel.FindEntityType(Type)</c>.
    /// </summary>
    /// <remarks>
    ///     For a type only known at runtime — a navigation's target, a query's element type. EF annotates
    ///     <c>FindEntityType(Type)</c> with <see cref="Entity" />, which a <see cref="Type" /> read off an expression
    ///     or an instance cannot satisfy; comparing <c>ClrType</c> needs no annotation at all. Same answer:
    ///     <c>FindEntityType(Type)</c> also returns only the entity type that has the CLR type to itself, never a
    ///     shared-type one.
    /// </remarks>
    public static IEntityType? FindEntityTypeOf(this IModel model, Type clrType)
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            if (entityType.ClrType == clrType && !entityType.HasSharedClrType)
            {
                return entityType;
            }
        }

        return null;
    }
}
