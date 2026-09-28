using Rask.Core.Diagnostics.DevTools;

namespace Rask.DevTools.Probe;

/// <summary>
///     One node in the tree the panel shows: a component, or an HTML element one of them rendered.
/// </summary>
/// <remarks>
///     A SNAPSHOT, not a live reference. The panel renders on its own session, after the walk that produced this has
///     finished, and a tree of live components read from there would be read while the app mutates it — and would keep
///     every component it named alive besides.
/// </remarks>
/// <param name="Id">Stable for as long as the component lives, so a panel's expansion survives a re-render.</param>
/// <param name="Type">The component's type name, as a developer writes it, or the element's tag.</param>
/// <param name="Key">Its reconciliation key, when it has one.</param>
/// <param name="Props">Its properties, as the build described them — empty in a build without the devtools.</param>
/// <param name="Children">What it rendered, in page order.</param>
/// <param name="IsTag">An HTML element rather than a component; the panel hides these unless asked.</param>
/// <param name="At">
///     Where it is on the page, as the client addresses DOM nodes: <c>path|firstSlot|count</c> — the child slots from the
///     document to its parent, dot-separated, the slot its first node occupies, and how many sibling nodes it rendered.
///     Null when it rendered nothing, sits inside another renderer's subtree, or the walk captured no frames.
/// </param>
/// <param name="Badge">
///     What kind of component it is, when that is more than a component: the runtime of an island (<c>React</c>,
///     <c>Lit</c>…) or <c>Blazor</c>. Read from the element it renders, so the devtools need no reference to either package.
/// </param>
/// <param name="Provides">
///     The context values its markup provides, in page order. Null when the render this came from recorded no context —
///     one before a panel was open.
/// </param>
/// <param name="Reads">The context values it read while rendering, once each; null as for <paramref name="Provides" />.</param>
internal sealed record DevToolsComponentNode(
    long Id,
    string Type,
    string? Key,
    IReadOnlyList<DescribedProp> Props,
    IReadOnlyList<DevToolsComponentNode> Children,
    bool IsTag = false,
    string? At = null,
    string? Badge = null,
    IReadOnlyList<DevToolsProvidedContext>? Provides = null,
    IReadOnlyList<DevToolsReadContext>? Reads = null);
