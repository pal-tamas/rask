using Rask.Core.Live;

namespace Rask.Core.Components;

/// <summary>
///     Hosts a component instance the application built itself, giving it the full lifecycle a component
///     built by its generated factory gets.
/// </summary>
/// <remarks>
///     Nearly every component enters the tree through its generated factory, and that factory's
///     <c>GetOrCreate</c> is what registers the instance with its parent and notifies it. A component
///     constructed some other way — the usual reason being that its type is not known until runtime, so
///     there is no factory to call: a plugin, a component chosen by name, one compiled at runtime — reaches
///     the tree as a plain object. It renders correctly, but it is invisible to the alive-set walk: no
///     <c>OnMount</c>, no <c>OnMountAsync</c>, no <c>OnRendered</c>, no <c>OnUnmount</c>, and no handle to
///     re-render through when an asynchronous hook completes. Anything that loads its data in
///     <c>OnMountAsync</c> therefore sits on its placeholder forever, with nothing reported.
///     <para>
///         Wrapping it fixes that: <c>Mount(Child: instance)</c> adopts the instance and notifies it, so it
///         behaves like any other child. Passing a component that <i>did</i> come from a generated factory
///         is harmless — it has already been adopted, and both steps here are no-ops for it.
///     </para>
///     <code>
///     var page = (Component)ActivatorUtilities.CreateInstance(services, pluginType);
///     return Div(Class: "host")[Mount(Child: page)];
///     </code>
/// </remarks>
public sealed class Mount : Component
{
    /// <summary>The instance to host. Rendered in place, with this component adding no markup of its own.</summary>
    public Component? Child { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        // Outside a live render there is no context to register with — serialization-only paths (ToHtml)
        // still render the child, they just have no lifecycle to run.
        if (Child is null || LiveRenderContext.Current is not { } context)
        {
            return Child;
        }

        // Exactly what the framework's own wrapper roots do for the components they forward to
        // (RootErrorBoundary for the App, RouteChainRenderer for a page, TestRoot for RaskTest.Render).
        AdoptChild(Child, RenderHandle);
        context.NotifyParameters(Child, propsChanged: false);
        return Child;
    }
}
