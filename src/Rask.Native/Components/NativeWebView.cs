using Rask.Core;

namespace Rask.Native.Components;

/// <summary>
///     The HTML surface of a native page — its children are the ordinary page content, which the native host
///     serializes into the platform WebView (Rask composes the document around the whole render, exactly as
///     on the web). Compose it as a sibling of the native bars:
///     <code>Render() => [NativeHeaderBar(Title: "Home"), NativeWebView()[Router()], NativeTabBar(...)];</code>
///     It renders its children transparently (like a fragment), so on a web host — where you branch with
///     <c>IsNative</c> and return the content alone — the same markup serializes identically. Only native
///     chrome (bars) may sit outside it; putting a bar inside its HTML content is a <c>RASK032</c> error.
/// </summary>
public sealed partial class NativeWebView : NativeComponent
{
    // Transparent HTML host: emit the children (the page shell) inline. A native component with children can't
    // reuse the render cache (see Component.RenderForLive), so this re-projects on each render.
    protected override Component? Render() => Children is null ? null : [.. Children];
}
