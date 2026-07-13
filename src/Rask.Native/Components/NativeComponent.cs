using Rask.Core;

namespace Rask.Native.Components;

/// <summary>
///     Base for the native-render family — components that describe real platform chrome (a header bar, a tab
///     bar, a toolbar) or host the WebView, rather than plain HTML. They are composed directly in a native
///     page's <c>Render()</c> tree, as siblings, e.g.
///     <code>Render() => [NativeHeaderBar(Title: "Home"), NativeWebView()[/* the HTML shell */], NativeTabBar(...)];</code>
///     The native host walks that tree: <see cref="NativeWebView" />'s children are serialized into the platform
///     WebView, while the surrounding bars are projected to a <c>UINavigationBar</c>/<c>UITabBar</c> (iOS) or a
///     top/bottom bar (Android). Every native component renders NO HTML by default (<see cref="Render" /> returns
///     <c>null</c>); <see cref="NativeWebView" /> is the one exception — it renders its children transparently.
///     <para>
///         The hierarchy is CLOSED (internal constructor): only this assembly can derive, so the native host's
///         descriptor builder can switch over a known-finite set of concrete types and user code cannot invent an
///         unknown bar the host wouldn't know how to project. Native components must not appear inside the HTML
///         tree (an element child / inside <see cref="NativeWebView" /> content) — that is the compile-time
///         <c>RASK032</c> guard.
///     </para>
/// </summary>
public abstract class NativeComponent : Component
{
    private protected NativeComponent() { }

    // Native components carry no HTML — the default render is nothing. NativeWebView overrides this to render
    // its children (the HTML shell) inline; the bars keep the null default.
    protected override Component? Render() => null;
}
