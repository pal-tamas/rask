using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Rask.Values;

namespace Rask.Data;

/// <summary>
///     Maps every <see cref="Timestamp" /> an entity declares as a column of the instant it holds, once the entity is added
///     to the model — overriding EF Core's relationship discovery, which takes the record for an entity of its own — on every
///     context of a Rask provider, whether or not it calls <c>ApplyRaskConventions</c>.
/// </summary>
/// <remarks>
///     The stamps (<c>CreatedAt</c>, <c>UpdatedAt</c>, <c>DeletedAt</c>) are <c>[NotMapped]</c> so a context of no Rask
///     provider is not handed a type it cannot map; on a Rask one they are columns, as the <c>DateTime</c> stamps always
///     were, and <c>ApplyRaskConventions</c> keeps only those the class's traits ask for.
/// </remarks>
internal sealed class TimestampConvention : IEntityTypeAddedConvention, IModelFinalizingConvention
{
    public void ProcessEntityTypeAdded(IConventionEntityTypeBuilder entityTypeBuilder, IConventionContext<IConventionEntityTypeBuilder> context) =>
        Map(entityTypeBuilder);

    // Again as the model is finalized: a [NotMapped] stamp is set aside again whenever the entity's base type is settled,
    // after the entity was added. One the app's configuration (or ApplyRaskConventions) ignored stays ignored.
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes().ToList())
        {
            Map(entityType.Builder);
        }
    }

    private static void Map(IConventionEntityTypeBuilder entityTypeBuilder)
    {
        var type = entityTypeBuilder.Metadata.ClrType;
        if (!typeof(IEntity).IsAssignableFrom(type))
        {
            return;
        }

        foreach (var member in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (member.PropertyType != typeof(Timestamp) || entityTypeBuilder.Metadata.FindProperty(member.Name) is { } mapped && mapped.DeclaringType != entityTypeBuilder.Metadata)
            {
                continue;
            }

            // [NotMapped] set a stamp aside for a context of no Rask provider; here it is a column. An ignore the app's
            // configuration (or ApplyRaskConventions, for a trait the class does not say) made stays.
            var ignored = entityTypeBuilder.Metadata.FindIgnoredConfigurationSource(member.Name);
            if (ignored == ConfigurationSource.Explicit)
            {
                continue;
            }

            if (ignored is not null)
            {
                ((IMutableEntityType)entityTypeBuilder.Metadata).RemoveIgnored(member.Name);
            }

            if (entityTypeBuilder.Property(member, fromDataAnnotation: true) is { } property && property.Metadata.GetValueConverter() is null)
            {
                var (converter, comparer) = ValueObjectColumn.TimestampConversion();
                property.HasConversion(converter, fromDataAnnotation: true);
                property.HasValueComparer(comparer, fromDataAnnotation: true);
            }
        }
    }
}
