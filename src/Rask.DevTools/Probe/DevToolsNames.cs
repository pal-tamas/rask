using System.Runtime.CompilerServices;
using Rask.Core.Diagnostics.DevTools;

namespace Rask.DevTools.Probe;

/// <summary>The names and ids the panel shows for components, shared by every tab so they agree.</summary>
internal static class DevToolsNames
{
    private static readonly ConditionalWeakTable<Type, string> Names = new();

    /// <summary><c>Ui.Tree&lt;Node, string&gt;</c> rather than <c>Ui.Tree`2</c>, and no namespace.</summary>
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

    /// <summary>A reason as the panel and the page's flash label write it.</summary>
    internal static string Label(DevToolsRenderReason reason) => reason switch
    {
        DevToolsRenderReason.Mount => "mount",
        DevToolsRenderReason.Props => "props",
        DevToolsRenderReason.State => "state",
        DevToolsRenderReason.Bypass => "bypass cache",
        DevToolsRenderReason.Context => "context",
        DevToolsRenderReason.Children => "children",
        _ => "uncached",
    };

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
