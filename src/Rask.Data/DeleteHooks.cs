using System.Collections.Concurrent;
using System.ComponentModel;

namespace Rask.Data;

/// <summary>
///     The aggregates' <c>OnDelete()</c> hooks, registered by generated code: what a save that deletes an aggregate calls
///     first, so the events it raises go out with the delete.
/// </summary>
/// <remarks>Public only so generated code can register; an application does not call it.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class DeleteHooks
{
    private static readonly ConcurrentDictionary<Type, Action<object>> Hooks = new();

    /// <summary>Whether any aggregate declares a hook — so a save that deletes nothing pays one read.</summary>
    internal static bool Any => !Hooks.IsEmpty;

    /// <summary>Registers <paramref name="aggregate" />'s <c>OnDelete()</c>.</summary>
    /// <param name="aggregate">The aggregate type.</param>
    /// <param name="onDelete">Calls its <c>OnDelete()</c> on the instance handed in.</param>
    public static void Register(Type aggregate, Action<object> onDelete)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(onDelete);
        Hooks[aggregate] = onDelete;
    }

    /// <summary>The hook of <paramref name="aggregate" />, or null when it declares none.</summary>
    internal static Action<object>? Of(Type aggregate) => Hooks.TryGetValue(aggregate, out var hook) ? hook : null;
}
