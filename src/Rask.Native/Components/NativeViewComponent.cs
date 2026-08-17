using System.Text;
using Rask.Core;
using Rask.Native.Surface;

namespace Rask.Native.Components;

/// <summary>
///     Base for the PURE-NATIVE view family — components that describe a real platform view (a stack, a label,
///     a button) rather than HTML or window chrome. A page built from these renders with no WebView at all: the
///     render walk turns the tree into <see cref="NativeNode" />s and an <see cref="INativeSurface" /> backend
///     materializes them as <c>UIView</c>s (iOS) or <c>android.view.View</c>s (Android).
/// </summary>
/// <remarks>
///     <para>
///         These compose inside a <see cref="NativeScreen" />, which is the pure-native counterpart of
///         <see cref="NativeWebView" /> and sits in the same slot — as a sibling of the native bars. Both may
///         appear in one app, on different routes, so a tab bar can hold a web page and a native screen side by
///         side; what a frame renders decides which surface it paints.
///     </para>
///     <para>
///         Like the rest of the native family the hierarchy is CLOSED (a <c>private protected</c> constructor),
///         so a backend's <c>switch</c> over <see cref="NativeNodeKind" /> stays exhaustive and user code cannot
///         invent a view the platform heads would not know how to build. Compose these into your own components
///         instead — a plain <c>Component</c> that renders native children is transparent to the walk, so
///         factoring a screen into <c>MyProfileCard</c> works exactly as it does on the web.
///     </para>
/// </remarks>
public abstract partial class NativeViewComponent : NativeComponent
{
    private protected NativeViewComponent() { }

    // Handler ids belong to the component INSTANCE, not to its position in the tree: a row removed above this
    // one must not renumber it and force a prop patch on every interactive view below. -1 means "never assigned"
    // — the builder hands out an id the first time it sees a component that actually carries a delegate.
    internal int SurfaceTapId = -1;
    internal int SurfaceChangeId = -1;

    /// <summary>The platform view this component describes.</summary>
    internal abstract NativeNodeKind SurfaceKind { get; }

    /// <summary>
    ///     Whether this component projects its <c>Children</c> as native child views. Containers say yes; a
    ///     leaf like <see cref="NativeLabel" /> says no, so nesting inside one renders nothing rather than
    ///     silently producing views the platform widget cannot host.
    /// </summary>
    internal virtual bool AcceptsChildren => false;

    /// <summary>Writes this component's props for the frame. Ids are already assigned when this runs.</summary>
    internal abstract void WriteSurfaceProps(ref NativePropWriter props);

    /// <summary>
    ///     The text a component takes from its children — how a label and a button get their content, so they
    ///     read like every other Rask component (<c>NativeLabel["Total"]</c>, the same spelling as
    ///     <c>Span["Total"]</c>) instead of carrying a <c>Text</c> property that no other component has.
    /// </summary>
    /// <remarks>
    ///     A bare <c>string</c> child is a <see cref="Rask.Core.Components.Text" />, so this reads their
    ///     values in order. Anything else in the children of a text-bearing leaf contributes nothing — those
    ///     components render no native view of their own.
    /// </remarks>
    private protected string? ChildText()
    {
        if (Children is null)
        {
            return null;
        }

        string? single = null;
        StringBuilder? many = null;
        foreach (var child in Children)
        {
            if (child is not Rask.Core.Components.Text { Value: { } value })
            {
                continue;
            }

            if (single is null)
            {
                single = value;
                continue;
            }

            many ??= new StringBuilder(single);
            many.Append(value);
        }

        return many?.ToString() ?? single;
    }

    // Containers render their children transparently so the render walk descends into them and reports each
    // nested native component — that pre-order stream is what the tree builder reconstructs the view tree from.
    // Leaves render nothing, exactly like the chrome components.
    /// <inheritdoc />
    protected override Component? Render() =>
        AcceptsChildren && Children is not null ? [.. Children] : null;
}
