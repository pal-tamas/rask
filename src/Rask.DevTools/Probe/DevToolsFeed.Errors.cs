using Rask.Core;

namespace Rask.DevTools.Probe;

internal sealed partial class DevToolsFeed
{
    /// <summary>The errors this page hit: its components' faults, and the framework diagnostics reported while it worked.</summary>
    internal DevToolsErrorLog Errors { get; } = new();

    /// <summary>The browser the page runs in, as its panel script named it (<c>Chrome 131</c>); null until it has.</summary>
    internal volatile string? Browser;

    /// <summary>
    ///     The components <paramref name="component" /> sat inside at the page's last render, innermost first, as the tree
    ///     names them — or nothing, when it was not in that render.
    /// </summary>
    /// <remarks>
    ///     Read without the render gate: it is asked for from a handler or a lifecycle hook that already failed, and waiting
    ///     for a render there could wait on the very dispatch that is failing. A handler and a render of the same page do not
    ///     run at once on either host, so a torn read needs a lifecycle hook faulting mid-render, and costs a wrong path in a
    ///     development tool, not a wrong page.
    /// </remarks>
    internal List<string> AncestorsOf(Component component)
    {
        var ancestors = new List<string>();
        var items = _capture.Items.ToArray();
        var parents = new Dictionary<Component, Component?>(items.Length, ReferenceEqualityComparer.Instance);
        foreach (var item in items)
        {
            parents.TryAdd(item.Component, item.Parent);
        }

        var root = _capture.Root;
        var at = component;
        // Bounded by the walk itself: a parent chain longer than the walk would be a cycle.
        for (var guard = 0; guard < items.Length && parents.TryGetValue(at, out var parent) && parent is not null; guard++)
        {
            if (ReferenceEquals(parent, root))
            {
                break;
            }

            ancestors.Add(DevToolsNames.Of(parent.GetType()));
            at = parent;
        }

        return ancestors;
    }
}
