namespace Rask.DevTools.Probe;

/// <summary>One component's render in a commit.</summary>
/// <param name="Id">The same id the Tree tab keys the component on, stable for as long as the component lives.</param>
/// <param name="Type">Its type name, as the Tree tab writes it.</param>
/// <param name="Key">Its reconciliation key, when it has one.</param>
/// <param name="Reason">Why it rendered.</param>
/// <param name="SelfTicks">
///     How long its own <c>Render()</c> took, in <see cref="System.Diagnostics.Stopwatch" /> ticks — not its children, which
///     render after it returns. -1 when the render threw.
/// </param>
/// <param name="At">
///     Where its nodes are on the page (<c>path|firstSlot|count</c>, as the Tree tab writes it), so the page can flash it.
///     Recorded only while a panel flashes renders; null otherwise, and for a component with no nodes of its own.
/// </param>
internal readonly record struct DevToolsRender(
    long Id, string Type, string? Key, DevToolsRenderReason Reason, long SelfTicks, string? At = null);
