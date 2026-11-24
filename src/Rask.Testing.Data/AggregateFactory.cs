using System.CodeDom.Compiler;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Rask.Testing;

/// <summary>
///     An aggregate's own factory — a static method declared on it that returns it, or a task of it, at any
///     accessibility — which is the one way <c>Test.Make</c> makes an aggregate, so its rules hold from the start.
/// </summary>
internal static class AggregateFactory
{
#pragma warning disable S3011 // A partial aggregate's factory is private by design (its generated entry takes the name); a test makes it through that one.
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
#pragma warning restore S3011

    /// <summary>The factory of <paramref name="aggregate" />, the one <paramref name="named" /> when it is given.</summary>
    /// <exception cref="InvalidOperationException">It has none, none of that name, or several and no name says which.</exception>
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "A test helper over the test's own aggregates, which a test project never trims.")]
    public static MethodInfo Of(Type aggregate, string? named)
    {
        var factories = aggregate.GetMethods(Declared).Where(m => IsFactory(aggregate, m)).ToArray();
        var picked = named is null ? factories : [.. factories.Where(m => string.Equals(m.Name, named, StringComparison.Ordinal))];

        return picked switch
        {
            [var only] => only,
            [] when named is not null => throw new InvalidOperationException(
                $"Test.Make<{aggregate.Name}>() was told o.Factory = \"{named}\", and {aggregate.Name} has no factory of that name; "
                + $"it has {Listed(factories)}."),
            [] => throw new InvalidOperationException(
                $"Test.Make<{aggregate.Name}>() makes an aggregate through its own factory, and {aggregate.Name} declares none: "
                + $"add a static method that returns a {aggregate.Name} (it may be private, as a partial aggregate's is), "
                + "or make this one by hand."),
            _ => throw new InvalidOperationException(
                $"Test.Make<{aggregate.Name}>() found several factories on {aggregate.Name} — {Listed(picked)} — "
                + $"so name one: Test.Make<{aggregate.Name}>(o => o.Factory = \"{picked[0].Name}\")."),
        };
    }

    /// <summary>What a factory hands back — the aggregate, a <c>Task</c> of it or a <c>ValueTask</c> of it — awaited.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "A test helper over the test's own aggregates, which a test project never trims.")]
    public static async Task<object> Awaited(object? made)
    {
        switch (made)
        {
            case null:
                throw new InvalidOperationException("The factory handed back null.");
            case Task task:
                await task.ConfigureAwait(false);
                return task.GetType().GetProperty(nameof(Task<>.Result))!.GetValue(task)!;
            case var value when value.GetType() is { IsGenericType: true } type && type.GetGenericTypeDefinition() == typeof(ValueTask<>):
                return await Awaited(type.GetMethod(nameof(ValueTask<>.AsTask))!.Invoke(value, null)).ConfigureAwait(false);
            default:
                return made;
        }
    }

    private static bool IsFactory(Type aggregate, MethodInfo method) =>
        !method.IsGenericMethodDefinition
        && !method.IsSpecialName
        && method.GetCustomAttribute<GeneratedCodeAttribute>() is null
        && method.GetCustomAttribute<CompilerGeneratedAttribute>() is null
        && Returns(aggregate, method.ReturnType)
        && !method.GetParameters().Any(static p => typeof(DbContext).IsAssignableFrom(p.ParameterType));

    private static bool Returns(Type aggregate, Type returned) =>
        returned == aggregate
        || (returned.IsGenericType
            && (returned.GetGenericTypeDefinition() == typeof(Task<>) || returned.GetGenericTypeDefinition() == typeof(ValueTask<>))
            && returned.GetGenericArguments()[0] == aggregate);

    private static string Listed(MethodInfo[] factories) =>
        factories.Length == 0 ? "none" : string.Join(", ", factories.Select(static f => f.Name + "(…)").Distinct(StringComparer.Ordinal));
}
