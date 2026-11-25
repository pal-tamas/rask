using System.Collections;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     Where each row of an aggregate sits, as its form model names it: <c>""</c> for the root, <c>"Lines[2]."</c> for
///     its third line, <c>"Lines[2].Taxes[0]."</c> below that — what a failure on a row is shown under.
/// </summary>
/// <remarks>
///     A row's place is its position in the collection that holds it now, which is the position the form posted it
///     at. A row the save deletes holds no place; nothing is asked of it.
/// </remarks>
internal sealed class RowPaths
{
    // The same ceiling AggregateChildren and AuditingInterceptor.RootOf walk with: deeper is several aggregates.
    private const int Deepest = 4;

    private readonly Dictionary<object, string> _paths = new(ReferenceEqualityComparer.Instance);

    private RowPaths()
    {
    }

    /// <summary>Every row <paramref name="root" /> holds, at any depth, with its place.</summary>
    /// <param name="rootType">The root's entity type.</param>
    /// <param name="root">The aggregate.</param>
    /// <returns>The places.</returns>
    internal static RowPaths Of(IEntityType rootType, object root)
    {
        var paths = new RowPaths();
        paths.Walk(rootType, root, "", 0);
        return paths;
    }

    /// <summary>The place of <paramref name="row" />; <c>""</c> for one the aggregate does not hold.</summary>
    internal string Of(object row) => _paths.GetValueOrDefault(row, "");

    private void Walk(IEntityType type, object entity, string path, int depth)
    {
        _paths[entity] = path;
        if (depth >= Deepest)
        {
            return;
        }

        foreach (var navigation in type.GetNavigations())
        {
            var target = navigation.TargetEntityType;
            if (target.IsOwned() || !AggregateChildren.IsChild(target.ClrType))
            {
                continue;
            }

            switch (navigation.GetGetter().GetClrValue(entity))
            {
                case IEnumerable children when navigation.IsCollection:
                    var index = 0;
                    foreach (var child in children)
                    {
                        Walk(target, child, path + navigation.Name + "[" + index.ToString(CultureInfo.InvariantCulture) + "].", depth + 1);
                        index++;
                    }

                    break;
                case { } child:
                    Walk(target, child, path + navigation.Name + ".", depth + 1);
                    break;
            }
        }
    }
}
