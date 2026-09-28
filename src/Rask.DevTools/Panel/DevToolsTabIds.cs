namespace Rask.DevTools.Panel;

/// <summary>
///     The panel's tab ids. A class of their own: inside markup, a component's name is its chain entry, not the type, so
///     constants on the component itself are out of reach there.
/// </summary>
internal static class DevToolsTabIds
{
    internal const string Wire = "wire";
    internal const string Tree = "tree";
    internal const string Renders = "renders";
    internal const string Perf = "perf";
    internal const string Errors = "errors";

    /// <summary>The <c>key</c> of the keydown the page's "Open in DevTools" arrives as.</summary>
    internal const string ShowErrorsKey = "errors:show";
}
