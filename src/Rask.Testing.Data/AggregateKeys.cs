using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Rask.Data;

namespace Rask.Testing;

/// <summary>
///     Which aggregate a key names: the one whose id is of a typed id's type (<c>TankId</c> is a <c>Tank</c>'s), or — for
///     a plain <c>Guid</c> or number — the one a parameter is named after (<c>companyId</c> is a <c>Company</c>'s).
/// </summary>
internal static class AggregateKeys
{
    // Each assembly's aggregates, read once: every parameter of every Make asks.
    private static readonly ConcurrentDictionary<Assembly, Type[]> Declared = new();

    /// <summary>The key type of <paramref name="aggregate" />: the <c>TId</c> of its <c>Entity&lt;TId&gt;</c>.</summary>
    public static Type KeyOf(Type aggregate)
    {
        for (var type = aggregate; type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Entity<>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        throw new InvalidOperationException($"{aggregate.Name} is no Entity<TId>, so it has no id to refer to it by.");
    }

    /// <summary>The id <paramref name="aggregate" /> was given.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "A test helper over the test's own aggregates, which a test project never trims.")]
    public static object IdOf(object aggregate) =>
        aggregate.GetType().GetProperty(nameof(Entity<>.Id))!.GetValue(aggregate)
        ?? throw new InvalidOperationException($"The {aggregate.GetType().Name} has no id.");

    /// <summary>
    ///     The aggregate, among those declared beside <paramref name="near" />, that a value of <paramref name="key" />
    ///     handed as <paramref name="parameter" /> refers to; null when it is no key.
    /// </summary>
    public static Type? Owner(Type key, string parameter, IEnumerable<Assembly> near)
    {
        var owners = near.Append(key.Assembly).Distinct().SelectMany(static a => Declared.GetOrAdd(a, Aggregates)).Where(a => KeyOf(a) == key).ToArray();
        if (!IsPlain(key))
        {
            return owners is [var only] ? only : null;
        }

        var named = parameter.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && parameter.Length > 2 ? parameter[..^2] : parameter;
        return owners.SingleOrDefault(a => string.Equals(a.Name, named, StringComparison.OrdinalIgnoreCase));
    }

    // A key any aggregate may share, so only a parameter's name tells whose it is.
    private static bool IsPlain(Type key) =>
        key.IsPrimitive || key == typeof(Guid) || key == typeof(string) || key == typeof(decimal);

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "A test helper over the test's own aggregates, which a test project never trims.")]
    private static Type[] Aggregates(Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException partly)
        {
            types = [.. partly.Types.OfType<Type>()];
        }

        return [.. types.Where(static t => t is { IsClass: true, IsAbstract: false } && typeof(IAggregate).IsAssignableFrom(t))];
    }
}
