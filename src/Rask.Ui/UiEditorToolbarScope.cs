namespace Rask;

/// <summary>
///     One toolbar's tab stop. A toolbar is a single stop in the tab order — its first control — and the arrow
///     keys walk the rest, so each control asks, in document order, whether the stop is still free.
/// </summary>
internal sealed class UiEditorToolbarScope
{
    private bool _taken;

    /// <summary>True for the first control that asks, false for every one after it.</summary>
    internal bool TakeTabStop()
    {
        var free = !_taken;
        _taken = true;
        return free;
    }
}
