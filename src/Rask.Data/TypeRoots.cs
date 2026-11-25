using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     The aggregate types a row of one entity type may belong to, read off the model: one for an aggregate, an owned
///     type or a child of one parent; one per parent key for a child two aggregates may hold (a Chip under a Driver or a
///     Vehicle), which the row itself then settles (<see cref="HeldBy" />).
/// </summary>
internal sealed class TypeRoots
{
    // The same ceiling the auditing walk uses: deeper than this is several aggregates under one name.
    private const int MaxDepth = 4;

    private readonly Type[] _roots;

    private TypeRoots(IForeignKey[] keys, Type[] roots, Type[] all)
    {
        Keys = keys;
        _roots = roots;
        All = all;
    }

    /// <summary>The parent keys that lead to different aggregates; empty when the type has one root, or none.</summary>
    internal IForeignKey[] Keys { get; }

    /// <summary>Every aggregate type a row may belong to, each once; the type itself when it is no aggregate's.</summary>
    internal Type[] All { get; }

    /// <summary>The aggregate type <paramref name="key" /> (one of <see cref="Keys" />) leads to.</summary>
    internal Type Of(IForeignKey key) => _roots[Array.IndexOf(Keys, key)];

    /// <summary>The roots of <paramref name="type" />.</summary>
    internal static TypeRoots Of(IEntityType type)
    {
        if (!typeof(IAggregate).IsAssignableFrom(type.ClrType) && type.FindOwnership() is null &&
            typeof(IEntity).IsAssignableFrom(type.ClrType))
        {
            var keys = new List<IForeignKey>();
            var roots = new List<Type>();
            foreach (var key in type.GetForeignKeys())
            {
                if (typeof(IEntity).IsAssignableFrom(key.PrincipalEntityType.ClrType) &&
                    FindRoot(key.PrincipalEntityType, 1) is { } root)
                {
                    keys.Add(key);
                    roots.Add(root);
                }
            }

            if (roots.Distinct().Count() > 1)
            {
                return new TypeRoots([.. keys], [.. roots], [.. roots.Distinct()]);
            }
        }

        return new TypeRoots([], [], [FindRoot(type, 0) ?? type.ClrType]);
    }

    private static Type? FindRoot(IReadOnlyEntityType type, int depth)
    {
        if (depth >= MaxDepth)
        {
            return null;
        }

        if (typeof(IAggregate).IsAssignableFrom(type.ClrType))
        {
            return type.ClrType;
        }

        if (type.FindOwnership() is { } ownership)
        {
            return FindRoot(ownership.PrincipalEntityType, depth + 1);
        }

        if (!typeof(IEntity).IsAssignableFrom(type.ClrType))
        {
            return null;
        }

        foreach (var principal in type.GetForeignKeys().Select(static foreignKey => foreignKey.PrincipalEntityType))
        {
            if (typeof(IEntity).IsAssignableFrom(principal.ClrType)
                && FindRoot(principal, depth + 1) is { } root)
            {
                return root;
            }
        }

        return null;
    }
}
