using System.Runtime.CompilerServices;
using Rask.Core;
using Rask.Core.Diagnostics.DevTools;

namespace Rask.DevTools.Probe;

/// <summary>Why a component rendered, as the Renders tab names it.</summary>
/// <remarks>
///     The runtime's <see cref="RenderCause" />, plus <see cref="Mount" />, which the runtime deliberately does not report:
///     an empty render cache is not a first render. The feed knows a mount as the first render it sees of a component.
/// </remarks>
internal enum DevToolsRenderReason : byte
{
    /// <summary>The first render the devtools saw of this component.</summary>
    Mount,

    /// <summary>Its parent passed changed props.</summary>
    Props,

    /// <summary><c>StateHasChanged</c>, or a handler that marked it dirty.</summary>
    State,

    /// <summary>It opts out of the render cache (<c>BypassRenderCache</c>).</summary>
    Bypass,

    /// <summary>It read context or culture during an earlier render.</summary>
    Context,

    /// <summary>It has children, whose cache cannot be trusted, so it renders with its parent.</summary>
    Children,

    /// <summary>Nothing marked it, and it had nothing cached: it renders null, or its subtree was kept as frames.</summary>
    Uncached,
}

/// <summary>One component's render in a commit.</summary>
/// <param name="Id">The same id the Tree tab keys the component on, stable for as long as the component lives.</param>
/// <param name="Type">Its type name, as the Tree tab writes it.</param>
/// <param name="Key">Its reconciliation key, when it has one.</param>
/// <param name="Reason">Why it rendered.</param>
/// <param name="SelfTicks">
///     How long its own <c>Render()</c> took, in <see cref="System.Diagnostics.Stopwatch" /> ticks — not its children, which
///     render after it returns. -1 when the render threw.
/// </param>
internal readonly record struct DevToolsRender(long Id, string Type, string? Key, DevToolsRenderReason Reason, long SelfTicks);

/// <summary>One render the page committed: every component whose <c>Render()</c> ran in it, in the order they ran.</summary>
/// <param name="Sequence">Monotonic per feed, counted with the wire events, so the two tabs' numbers never collide.</param>
/// <param name="Timestamp">When it committed, as a <see cref="System.Diagnostics.Stopwatch" /> timestamp.</param>
/// <param name="Walked">How many components the walk passed through, rendered or served from their cache.</param>
/// <param name="Renders">The components that actually rendered.</param>
internal sealed record DevToolsCommit(long Sequence, long Timestamp, int Walked, DevToolsRender[] Renders);

/// <summary>A render the walk in progress ran, before the commit it belongs to is known.</summary>
internal record struct DevToolsRenderItem(Component Component, RenderCause Cause, long SelfTicks);

/// <summary>The names and ids the panel shows for components, shared by every tab so they agree.</summary>
internal static class DevToolsNames
{
    private static readonly ConditionalWeakTable<Type, string> Names = new();

    /// <summary><c>UiTree&lt;Node, string&gt;</c> rather than <c>UiTree`2</c>, and no namespace.</summary>
    internal static string Of(Type type) => Names.GetValue(type, Build);

    private static string Build(Type type)
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

        return name + "<" + string.Join(", ", type.GetGenericArguments().Select(Of)) + ">";
    }

    /// <summary>The runtime's cause, in the tab's words.</summary>
    internal static DevToolsRenderReason ReasonOf(RenderCause cause) => cause switch
    {
        RenderCause.Props => DevToolsRenderReason.Props,
        RenderCause.State => DevToolsRenderReason.State,
        RenderCause.Forced => DevToolsRenderReason.Bypass,
        RenderCause.AmbientState => DevToolsRenderReason.Context,
        RenderCause.Children => DevToolsRenderReason.Children,
        _ => DevToolsRenderReason.Uncached,
    };
}
