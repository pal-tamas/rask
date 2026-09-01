using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Rask.Blazor;

/// <summary>
///     Renders one hosted Blazor component to HTML, and keeps it alive across Rask prop changes.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="StaticHtmlRenderer" /> rather than the friendlier <c>HtmlRenderer</c>, and that
///         is the whole design. <c>HtmlRenderer</c> hands back an <c>HtmlRootComponent</c> whose
///         entire surface is <c>QuiescenceTask</c> / <c>ToHtmlString</c> / <c>WriteHtmlTo</c> — there
///         is no way to push new parameters into a root it already rendered. The only way to show a
///         changed value would be to render the component again as a NEW root, which re-runs
///         <c>OnInitializedAsync</c> and throws away everything the component had accumulated.
///         <c>Renderer.RenderRootComponentAsync(int, ParameterView)</c> is what makes an
///         update an update: same component instance, new parameters, <c>OnParametersSet</c> rather
///         than <c>OnInitialized</c>.
///     </para>
///     <para>
///         One renderer per island instance rather than one per page or per session. A renderer
///         serializes all its work through a single dispatcher queue, so a shared one would let a
///         slow island block every other island's first paint — and the page's quiescence budget is
///         for the whole page, not per island.
///     </para>
/// </remarks>
internal sealed class BlazorIslandRenderer : StaticHtmlRenderer
{
    private readonly Action _onSelfRender;

    public BlazorIslandRenderer(IServiceProvider services, ILoggerFactory loggerFactory, Action onSelfRender)
        : base(services, loggerFactory) =>
        _onSelfRender = onSelfRender;

    /// <summary>Adopts a component instance and returns the id every later call addresses it by.</summary>
    public int Attach(IComponent component) => AssignRootComponentId(component);

    /// <summary>Mounts or updates the root with a new parameter set.</summary>
    /// <remarks>
    ///     Must be awaited on <c>Dispatcher</c>. Never block on it from Rask's
    ///     synchronous serialize walk: <c>Dispatcher.InvokeAsync(...).GetAwaiter().GetResult()</c>
    ///     deadlocks against the renderer's own synchronization context, which is why
    ///     <c>benchmarks/Rask.Benchmarks.VsBlazor</c> had to supply an inline dispatcher of its own.
    ///     Everything here is reached from <c>OnPropsChangedAsync</c>, which is already async.
    /// </remarks>
    public Task RenderAsync(int componentId, ParameterView parameters) =>
        RenderRootComponentAsync(componentId, parameters);

    /// <summary>Releases the root. Pairs with <see cref="Attach" />.</summary>
    public void Detach(int componentId) => RemoveRootComponent(componentId);

    /// <summary>
    ///     The component's current render tree, which is where its event-handler ids live.
    /// </summary>
    /// <remarks>
    ///     Blazor assigns a real handler id to every <c>@onclick</c> even in a static render; it is
    ///     only the built-in HTML writer that drops them. Exposing the frames is what lets
    ///     <see cref="BlazorFrameWriter" /> keep those handlers alive by writing Rask's own
    ///     <c>data-rask-on-*</c> instead.
    /// </remarks>
    public Microsoft.AspNetCore.Components.RenderTree.ArrayRange<
        Microsoft.AspNetCore.Components.RenderTree.RenderTreeFrame> FramesFor(int componentId) =>
        GetCurrentRenderTreeFrames(componentId);

    /// <summary>Dispatches a browser event back into the hosted component.</summary>
    public Task DispatchAsync(ulong handlerId, EventArgs eventArgs) =>
        DispatchEventAsync(handlerId, null!, eventArgs, waitForQuiescence: true);

    /// <summary>The argument type Blazor expects for a handler, so the payload can be built.</summary>
    public Type ArgsTypeFor(ulong handlerId) => GetEventArgsType(handlerId);

    /// <summary>The component's current HTML.</summary>
    public string Html(int componentId)
    {
        var writer = new StringWriter();
        WriteComponentHtml(componentId, writer);
        return writer.ToString();
    }

    /// <summary>
    ///     A render this island did not ask for — the hosted component called
    ///     <c>StateHasChanged</c> itself.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A timer, or an event from an injected service, produces a batch that nothing else in
    ///         Rask would ever notice. The batch itself is of no use to us — we re-read the HTML
    ///         rather than translating Blazor's edits — but the NOTIFICATION is: without it the
    ///         component would update its own tree on the server and the browser would never hear.
    ///     </para>
    ///     <para>
    ///         This is the one thing in the package that touches
    ///         <c>Microsoft.AspNetCore.Components.RenderTree</c>, which is what BL0006 warns about.
    ///         The suppression has to be the project (see the csproj), not this signature: naming this
    ///         class anywhere — even <c>new BlazorIslandRenderer(...)</c> — reports it too, because the
    ///         override puts a RenderTree type in the class's own surface.
    ///     </para>
    ///     <para>
    ///         What keeps that safe is that we never read <c>renderBatch</c>, index into it, or depend
    ///         on its shape; the body ignores it entirely and re-reads the component's HTML instead. It
    ///         is named solely because overriding the base method requires spelling its parameter type.
    ///         So a future change to those types cannot silently alter what this does — it can only
    ///         fail to compile.
    ///     </para>
    /// </remarks>
    protected override Task UpdateDisplayAsync(in Microsoft.AspNetCore.Components.RenderTree.RenderBatch renderBatch)
    {
        _onSelfRender();
        return Task.CompletedTask;
    }

    /// <summary>
    ///     An exception from inside the hosted component's own lifecycle.
    /// </summary>
    /// <remarks>
    ///     Rethrown rather than swallowed. A hosted component that threw in
    ///     <c>OnInitializedAsync</c> has produced no markup, and rendering the island empty would put
    ///     a silently blank region on the page — the failure shape this repo keeps rediscovering.
    ///     Letting it out surfaces it through the awaiting lifecycle hook, where Rask's own error
    ///     boundary can see it.
    /// </remarks>
    protected override void HandleException(Exception exception) =>
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
}
