using System.Runtime.CompilerServices;
using Rask.Core;

namespace Rask.DevTools.Probe;

/// <summary>
///     One component in the tree the panel shows: what it is, what identifies it, and what it rendered.
/// </summary>
/// <remarks>
///     A SNAPSHOT, not a live reference. The panel renders on its own session, after the walk that produced this has
///     finished, and a tree of live components read from there would be read while the app mutates it — and would keep
///     every component it named alive besides.
/// </remarks>
/// <param name="Id">Stable for as long as the component lives, so a panel's expansion survives a re-render.</param>
/// <param name="Type">The component's type name, as a developer writes it.</param>
/// <param name="Key">Its reconciliation key, when it has one.</param>
/// <param name="Children">What it rendered, in render order.</param>
internal sealed record DevToolsComponentNode(
    int Id,
    string Type,
    string? Key,
    IReadOnlyList<DevToolsComponentNode> Children);

/// <summary>
///     Turns a live component tree into a snapshot, and hands every component an id that outlives one render.
/// </summary>
internal sealed class DevToolsTreeSnapshotter
{
    /// <summary>How many components one snapshot may carry. A tree past this is truncated rather than left to grow.</summary>
    internal const int MaxNodes = 5000;

    // Weak, and by reference: the id belongs to the component, and a component that leaves the tree takes its id with it.
    private readonly ConditionalWeakTable<Component, StrongBox<int>> _ids = new();
    private int _next;

    internal DevToolsComponentNode Snapshot(Component root)
    {
        var budget = MaxNodes;
        return Build(root, ref budget);
    }

    private DevToolsComponentNode Build(Component component, ref int budget)
    {
        budget--;
        var children = new List<DevToolsComponentNode>();
        foreach (var child in component.PersistedChildren.Values)
        {
            if (budget <= 0)
            {
                break;
            }

            children.Add(Build(child, ref budget));
        }

        return new DevToolsComponentNode(IdOf(component), Name(component.GetType()), component.Key?.ToString(), children);
    }

    private int IdOf(Component component) => _ids.GetValue(component, _ => new StrongBox<int>(Interlocked.Increment(ref _next))).Value;

    // `UiTree<Node, string>` rather than `UiTree\`2`, and no namespace: a tree of full names reads as one column of noise.
    private static string Name(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var name = type.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0)
        {
            name = name[..tick];
        }

        return name + "<" + string.Join(", ", type.GetGenericArguments().Select(Name)) + ">";
    }
}
