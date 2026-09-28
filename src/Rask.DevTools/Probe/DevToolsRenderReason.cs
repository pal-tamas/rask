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
