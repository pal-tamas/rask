namespace Rask.Core.Live;

/// <summary>
///     Ambient scope holder for the currently-active <see cref="FrameWriter" />. Lets
///     code paths that don't take the writer as an explicit parameter (notably the
///     <c>WriteAttributes(StringBuilder)</c> overrides on the 48 HTML-element
///     subclasses) emit Attribute frames without a signature change to every subclass.
///     The render walk is single-threaded per session so a <see cref="ThreadStaticAttribute" />
///     is the right shape; the <see cref="Push" /> / <see cref="IDisposable.Dispose" />
///     pattern handles re-entry (error-boundary catch-and-replay, nested ToHtml calls).
/// </summary>
public static class FrameSinkScope
{
    [field: ThreadStatic] public static FrameWriter? Current { get; private set; }

    public static Popper Push(FrameWriter? writer)
    {
        var prev = Current;
        Current = writer;
        return new Popper(prev);
    }

    public readonly struct Popper(FrameWriter? previous) : IDisposable
    {
        public void Dispose() => Current = previous;
    }
}
