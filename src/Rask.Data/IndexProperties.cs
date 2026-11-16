using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>An index's members as the scalar properties every rule here reads.</summary>
/// <remarks>
///     EF Core 11 lets an index name a complex property, so <c>IIndex.Properties</c> is a list of
///     <see cref="IReadOnlyPropertyBase" />. A column name, a stored value and a length belong to a scalar property only,
///     so a rule that reads them asks for the scalars and leaves an index naming anything else to the database.
/// </remarks>
internal static class IndexProperties
{
    /// <summary>
    ///     <paramref name="members" /> as <typeparamref name="TProperty" />, or <see langword="null" /> when any of
    ///     them is not one.
    /// </summary>
    internal static TProperty[]? Scalars<TProperty>(IReadOnlyList<IReadOnlyPropertyBase> members)
        where TProperty : class, IReadOnlyProperty
    {
        var scalars = new TProperty[members.Count];
        for (var i = 0; i < scalars.Length; i++)
        {
            if (members[i] is not TProperty scalar)
            {
                return null;
            }

            scalars[i] = scalar;
        }

        return scalars;
    }
}
