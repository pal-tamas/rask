namespace Rask.Core.Diagnostics.DevTools;

/// <summary>
///     Why a component's <c>Render()</c> actually ran, rather than being served from its cache. Checked in declaration
///     order; the first that applies is the cause.
/// </summary>
/// <remarks>
///     There is deliberately no <c>OnMount</c>. "Nothing cached" is not the same as "first render": a live session captures a
///     clean pure-element subtree as frames and drops the component's cached result, and a component that renders null
///     never has one. Reporting those as mounts would call every such re-render a mount. The devtools recognise a mount as
///     the first time they see a component render.
/// </remarks>
internal enum RenderCause : byte
{
    /// <summary>A parent passed changed props.</summary>
    Props,

    /// <summary><c>StateHasChanged</c>, or a handler that marked the component dirty.</summary>
    State,

    /// <summary>The component opts out of the render cache (<c>BypassRenderCache</c>).</summary>
    Forced,

    /// <summary>It read ambient state (context, culture) during an earlier render.</summary>
    AmbientState,

    /// <summary>A non-element component with children, whose cache cannot be trusted.</summary>
    Children,

    /// <summary>
    ///     None of the above, so the render cache was empty: a first render, a subtree whose cache was captured as frames,
    ///     or a component that renders null.
    /// </summary>
    Uncached,
}
