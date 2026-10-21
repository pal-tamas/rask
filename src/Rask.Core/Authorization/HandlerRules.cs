using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;

namespace Rask.Core.Authorization;

/// <summary>
///     What a handler's <c>[Authorize]</c> asks, read once per method, and the guard a method group is held behind.
/// </summary>
/// <remarks>
///     <para>
///         Asked wherever a handler enters a <see cref="Callback" /> — every render — so a handler without the
///         attribute must cost nothing: its receiver's type is looked up once, and only a component that declares
///         a guarded method ever has a delegate's method read. A lambda never carries the attribute; the call it
///         makes to a guarded method is guarded where it is written, by the generated interceptor.
///     </para>
///     <para>
///         A method's attributes only change under hot reload, which calls <see cref="Forget" />.
///     </para>
/// </remarks>
internal static class HandlerRules
{
    private const string KeptHandlers =
        "A method the trimmer removed is referenced by no delegate, so it is never a handler to guard; a kept method keeps its attributes.";

    private static readonly ConcurrentDictionary<Type, bool> DeclaresGuarded = new();
    private static readonly ConcurrentDictionary<MethodInfo, IAuthorizeData[]?> ByMethod = new();

    /// <summary>What <paramref name="handler" />'s method asks of the person who fires it, or null for nothing.</summary>
    public static IAuthorizeData[]? Of(Delegate? handler)
    {
        if (handler is null)
        {
            return null;
        }

        // A method group's receiver is the component; a static method has none and is read directly.
        if (handler.Target is { } target && !DeclaresGuarded.GetOrAdd(target.GetType(), static type => Declares(type)))
        {
            return null;
        }

        return Of(handler.Method);
    }

    /// <summary>What <paramref name="method" /> asks of the person who calls it, or null for nothing.</summary>
    public static IAuthorizeData[]? Of(MethodInfo method) => ByMethod.GetOrAdd(method, static method => Read(method));

    /// <summary>The method's name as the log says it: <c>Page.Remove</c>.</summary>
    public static string Name(Delegate handler) => Name(handler.Method);

    /// <inheritdoc cref="Name(Delegate)" />
    public static string Name(MethodInfo method) => method.DeclaringType?.Name + "." + method.Name;

    public static void Forget()
    {
        DeclaresGuarded.Clear();
        ByMethod.Clear();
    }

    private static IAuthorizeData[]? Read(MethodInfo method)
    {
        if (method.DeclaringType is not { } declaring || !typeof(Component).IsAssignableFrom(declaring)
            || method.IsDefined(typeof(IAllowAnonymous), true))
        {
            return null;
        }

        IAuthorizeData[] rule = [.. method.GetCustomAttributes(true).OfType<IAuthorizeData>()];
        return rule.Length == 0 ? null : rule;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = KeptHandlers)]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = KeptHandlers)]
    private static bool Declares(Type type)
    {
        if (!typeof(Component).IsAssignableFrom(type))
        {
            return false;
        }

#pragma warning disable S3011 // a handler is usually a private method: finding its attribute is the point, nothing is called
        const BindingFlags all = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static
                                 | BindingFlags.Public | BindingFlags.NonPublic;
#pragma warning restore S3011
        for (var at = type; at is not null && at != typeof(Component); at = at.BaseType)
        {
            if (at.GetMethods(all).Any(static method => Read(method) is not null))
            {
                return true;
            }
        }

        return false;
    }
}
