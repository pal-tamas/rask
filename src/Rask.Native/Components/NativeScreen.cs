using Rask.Native.Surface;

namespace Rask.Native.Components;

/// <summary>
///     The content root of a PURE-NATIVE page — the counterpart of <see cref="NativeWebView" />, and its
///     sibling in the same slot. Everything inside it is real platform views; there is no WebView, no HTML and
///     no JavaScript on the frames it paints.
/// </summary>
/// <remarks>
///     A single app mixes the two freely: compose a <see cref="NativeWebView" /> on the routes you want served
///     as HTML and a <see cref="NativeScreen" /> on the ones you want fully native, and the host swaps surfaces
///     as you navigate. Neither surface is torn down on the switch, so returning to a web route does not reload
///     the page. Routing itself is unchanged — <c>Router()</c> works inside a screen exactly as it does on the
///     web, so <c>Features.Routes.*</c> links, route parameters and guards all behave the same.
/// </remarks>
/// <example>
///     <code>Render() => [NativeHeaderBar(Title: "Profile"),
///                     NativeScreen()[Router()],
///                     NativeTabBar(Tabs: [...])];</code>
/// </example>
public sealed partial class NativeScreen : NativeViewComponent
{
    /// <summary>The screen's background color. Leave <c>null</c> for the platform's system background.</summary>
    public NativeColor? Background { get; set; }

    /// <summary>Uniform inner padding in points. Leave <c>null</c> for none.</summary>
    public double? Padding { get; set; }

    /// <inheritdoc />
    internal override NativeNodeKind SurfaceKind => NativeNodeKind.Screen;

    /// <inheritdoc />
    internal override bool AcceptsChildren => true;

    /// <inheritdoc />
    internal override void WriteSurfaceProps(ref NativePropWriter props)
    {
        props.Color(NativePropId.Background, Background);
        props.Number(NativePropId.Padding, Padding);
    }
}
