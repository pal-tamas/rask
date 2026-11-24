using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Rask.Data;

/// <summary>
///     What a child reads as through its public properties, taken before and after an update, so
///     <c>Children.Sync</c> tells a child its update changed from one it left as it was.
/// </summary>
/// <remarks>
///     Values compare by <see cref="object.Equals(object, object)" />: a value object or a typed id by its value, a
///     collection by reference — a child that holds a collection of its own is changed when the update gives it another.
/// </remarks>
/// <typeparam name="TEntity">The child.</typeparam>
internal static class ChildState<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TEntity>
{
    private static readonly PropertyInfo[] Readable =
        [.. typeof(TEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static p => p.CanRead && p.GetIndexParameters().Length == 0)];

    /// <summary>The values <paramref name="child" /> reads as now.</summary>
    internal static object?[] Of(TEntity child) => [.. Readable.Select(p => p.GetValue(child))];

    /// <summary>Whether two readings are the same.</summary>
    internal static bool Same(object?[] before, object?[] after) => before.SequenceEqual(after);
}
