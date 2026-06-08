namespace Rask.Core.Components;

/// <summary>
///     Ambient stack of context values, active during the synchronous render walk.
///     <see cref="HtmlSerializer" /> pushes an entry when it enters a <see cref="Context" />
///     provider subtree and pops it on exit (balanced <c>using</c>), so a descendant's
///     <see cref="Component.Render" /> — which executes inside that walk — observes the nearest
///     enclosing provider. Mirrors <see cref="Rask.Core.Forms.EditContextScope" /> but holds a
///     linked stack so nested and differently-typed providers coexist.
///     <para>
///         Named <c>ContextStack</c> (not <c>ContextScope</c>) to avoid colliding with the
///         nested <c>LiveRenderContext.ContextScope</c> pop helper.
///     </para>
/// </summary>
internal static class ContextStack
{
    private static readonly AsyncLocal<Entry?> _head = new();

    internal static Entry? Head => _head.Value;

    internal static IDisposable Push(Type valueType, string? name, object? value)
    {
        var prev = _head.Value;
        _head.Value = new Entry(valueType, name, value, prev);
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
        for (var e = _head.Value; e is not null; e = e.Parent)
        {
            if (e.Name == name && requested.IsAssignableFrom(e.ValueType))
            {
                value = e.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    internal sealed record Entry(Type ValueType, string? Name, object? Value, Entry? Parent);

    private sealed class Popper(Entry? prev) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _head.Value = prev;
        }
    }
}
