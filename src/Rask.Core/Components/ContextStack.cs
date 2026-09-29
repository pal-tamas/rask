namespace Rask.Core.Components;

/// <summary>
///     Ambient stack of context values, active during the synchronous render walk.
///     <see cref="HtmlSerializer" /> pushes an entry when it enters a <see cref="Context" />
///     provider subtree and pops it on exit (balanced <c>using</c>), so a descendant's
///     <see cref="Component.Render" /> — which executes inside that walk — observes the nearest
///     enclosing provider. Mirrors <see cref="Rask.Core.Forms.EditContextScope" /> but holds a
///     linked stack so nested and differently-typed providers coexist.
///     <para>
///         Thread-static, not <see cref="AsyncLocal{T}" />: the walk never awaits, so the thread IS the
///         walk, and an <see cref="AsyncLocal{T}" /> write copies the execution context's value map —
///         twice per provider per render. The price is that context answers only inside the walk, which
///         is where <c>Context.Get</c> is documented to be called.
///     </para>
///     <para>
///         Named <c>ContextStack</c> (not <c>ContextScope</c>) to avoid colliding with the
///         nested <c>LiveRenderContext.ContextScope</c> pop helper.
///     </para>
/// </summary>
internal static class ContextStack
{
    [ThreadStatic] private static Entry? t_head;

    internal static Entry? Head
    {
        get => t_head;
        private set => t_head = value;
    }

    internal static Popper Push(Type valueType, string? name, object? value)
    {
        var prev = t_head;
        t_head = new Entry(valueType, name, value, prev);
        return new Popper(prev);
    }

    /// <summary>
    ///     Resolve the nearest provider whose declared type is assignable to
    ///     <paramref name="requested" /> and whose name matches <paramref name="name" />.
    ///     Returns <c>true</c> even when the provided <paramref name="value" /> is null (a
    ///     provider explicitly supplying null is distinct from no provider at all).
    /// </summary>
    internal static bool TryGet(Type requested, string? name, out object? value)
    {
        if (Find(requested, name) is { } entry)
        {
            value = entry.Value;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>The entry <see cref="TryGet" /> resolves to, or null. The devtools read which provider answered.</summary>
    internal static Entry? Find(Type requested, string? name)
    {
        for (var e = t_head; e is not null; e = e.Parent)
        {
            if (string.Equals(e.Name, name, StringComparison.Ordinal) && requested.IsAssignableFrom(e.ValueType))
            {
                return e;
            }
        }

        return null;
    }

    internal sealed record Entry(Type ValueType, string? Name, object? Value, Entry? Parent);

    internal readonly struct Popper(Entry? prev) : IDisposable
    {
        public void Dispose() => Head = prev;
    }
}
