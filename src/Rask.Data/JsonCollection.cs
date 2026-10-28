using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

/// <summary>A collection of value objects mapped as one JSON column: an EF Core complex collection.</summary>
/// <remarks>
/// A complex collection rather than an owned one, because its column is NOT NULL where the model's property is
/// non-nullable — as every other column is. An owned collection's column is NULL whatever the model says, and EF
/// Core refuses to be told otherwise. EF Core reads and writes a complex collection through an <c>IList&lt;T&gt;</c>
/// member, so the read-only view needs a list behind it: the <c>List&lt;T&gt;</c> field Rask recommends, or a
/// property that is a list itself.
/// </remarks>
internal static class JsonCollection
{
    /// <summary>
    /// The list EF Core can write <paramref name="property" /> through — <paramref name="field" />'s type, or the
    /// property's own — or null when neither is an <c>IList&lt;T&gt;</c> of <paramref name="element" />.
    /// </summary>
    /// <param name="clrType">The entity's type.</param>
    /// <param name="property">The property's name.</param>
    /// <param name="field">The field behind it, or null.</param>
    /// <param name="element">The value object it holds.</param>
    [return: DynamicallyAccessedMembers(DataTrimming.Entity)]
    public static Type? ListBehind(
        [DynamicallyAccessedMembers(DataTrimming.Entity)] Type clrType, string property, string? field, Type element)
    {
        var declared = field is null ? PropertyType(clrType, property) : FieldType(clrType, field);

        return declared is not null && declared.GetInterfaces().Append(declared).Any(t =>
            t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IList<>) && t.GetGenericArguments()[0] == element)
            ? declared
            : null;
    }

    /// <summary>Maps <paramref name="property" /> as a JSON collection held in <paramref name="list" />.</summary>
    /// <param name="builder">The entity holding it.</param>
    /// <param name="list">The list type EF Core reads and writes it through.</param>
    /// <param name="property">The property's name.</param>
    public static ComplexCollectionBuilder Map(
        EntityTypeBuilder builder,
        [DynamicallyAccessedMembers(DataTrimming.Entity)] Type list,
        string property)
    {
        var values = builder.ComplexCollection(list, property);
        values.ToJson();
        return values;
    }

    // The bases are walked as EF Core's own member discovery walks them, for the same entity under the same annotation.
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "See above.")]
    [UnconditionalSuppressMessage("Trimming", "IL2073", Justification = "The member's type is the one EF Core reflects over itself.")]
    [return: DynamicallyAccessedMembers(DataTrimming.Entity)]
    private static Type? PropertyType([DynamicallyAccessedMembers(DataTrimming.Entity)] Type clrType, string name)
    {
        for (var type = clrType; type is not null; type = type.BaseType)
        {
            if (type.GetProperty(name, Declared) is { } found)
            {
                return found.PropertyType;
            }
        }

        return null;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "See PropertyType.")]
    [UnconditionalSuppressMessage("Trimming", "IL2073", Justification = "The member's type is the one EF Core reflects over itself.")]
    [return: DynamicallyAccessedMembers(DataTrimming.Entity)]
    private static Type? FieldType([DynamicallyAccessedMembers(DataTrimming.Entity)] Type clrType, string name)
    {
        for (var type = clrType; type is not null; type = type.BaseType)
        {
            if (type.GetField(name, Declared) is { } found)
            {
                return found.FieldType;
            }
        }

        return null;
    }

#pragma warning disable S3011 // the field behind a read-only view is private by design; EF Core reads it the same way
    private const BindingFlags Declared =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
#pragma warning restore S3011
}
