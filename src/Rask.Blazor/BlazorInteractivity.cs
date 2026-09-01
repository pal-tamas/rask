namespace Rask.Blazor;

/// <summary>How much of the hosted Blazor component's own behaviour reaches the browser.</summary>
public enum BlazorInteractivity
{
    /// <summary>
    ///     The component renders on the server and its HTML is delivered as ordinary markup. The
    ///     default.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Lifecycle up to and including <c>BuildRenderTree</c> runs — <c>OnInitialized</c>,
    ///         <c>OnInitializedAsync</c> and <c>OnParametersSet</c> all fire, and an <c>await</c> in
    ///         an async hook is finished before the page is sent, so the component is complete in the
    ///         FIRST response rather than appearing a frame later.
    ///     </para>
    ///     <para>
    ///         What does not run is anything needing a browser: <c>OnAfterRender</c>,
    ///         <c>@onclick</c>, <c>@bind</c>, <c>IJSRuntime</c>, <c>ElementReference</c>. A component
    ///         built out of markup and parameters (a card, a table, a chart) renders correctly; one
    ///         whose behaviour is its point (a menu, a dialog, an autocomplete) renders inert.
    ///     </para>
    ///     <para>
    ///         Rask children placed inside the component are NOT inert — their handlers are Rask's
    ///         own and reach the page through the same delegated channel as any other element. The
    ///         useful shape is therefore to let the Blazor component be chrome and keep the
    ///         interactive parts in Rask.
    ///     </para>
    /// </remarks>
    Static,

    /// <summary>
    ///     Reserved. The component will be handed to a Blazor circuit so its own events work.
    /// </summary>
    /// <remarks>
    ///     Not implemented yet: selecting it is reported by RASK067. It exists in the enum now
    ///     because <c>BlazorHost.Opaque</c> already keys the diff boundary off
    ///     it — a circuit-hosted subtree belongs to Blazor's renderer and Rask must not diff into it,
    ///     while a statically rendered one is Rask's own markup and must be diffed normally.
    /// </remarks>
    Circuit,
}
