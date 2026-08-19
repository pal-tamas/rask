using Rask.Chrome.Components;
using Rask.Core;

namespace Rask.Chrome;

/// <summary>
///     A routed component that also declares the chrome around it — the header bar, toolbar and tab bar.
///     Fill the slots with the portable bars (<see cref="AppBar" /> /
///     <see cref="TabStrip" />) and one screen class serves every host: the web hosts render
///     landmark markup, and a native host projects the same declaration to real platform widgets (a
///     <c>UINavigationBar</c>/<c>UITabBar</c> on iOS, a top/bottom bar on Android).
///     <code>
/// [Route("/todos")]
/// public sealed class TodosScreen : Screen
/// {
///     protected override Component? HeaderBar => AppBar.Title("Todos");
///
///     protected override Component? Render() => Div[/* the HTML body */];
/// }
///     </code>
///     <para>
///         A screen is routed exactly like any other page — navigation on native is the same path change, and
///         <c>TodosScreen.Url()</c> / <c>TodosScreen.Go()</c> are generated the same way. The path is simply
///         never shown to the user: the WebView sits on a custom-scheme origin, and a deep link (App Links /
///         Universal Links) maps an external URL onto the same template.
///     </para>
///     <para>
///         The slots are hoisted, not rendered inline: they are walked inside the screen's own scope — so a bar
///         button's <c>OnClick</c> attributes back to the screen and re-renders it like any callback. This is
///         what lets a screen own its chrome instead of the app root inspecting the current path to decide what
///         the header should say.
///     </para>
///     <para>
///         Chrome merges by kind, deepest-wins, so composition falls out of the route chain: a layout screen
///         (a <c>[ParentRoute]</c> layout) supplies the <see cref="TabBar" /> once and each leaf screen supplies
///         its own <see cref="HeaderBar" />.
///     </para>
///     <para>
///         The slots are walked on <b>every</b> host, and what they render is the bar's own business — no
///         <c>IsNative</c> branching in your screen. The portable bars render markup on the web and nothing
///         inside a native shell (where they are a platform widget instead); the platform-exact
///         <c>Rask.Native</c> bars render nothing anywhere, so a native-only screen still emits no HTML.
///         A screen that declares no chrome pays a null check.
///     </para>
///     <para>
///         Reach for the <c>Rask.Native</c> family (<c>NativeHeaderBar</c>, <c>NativeTabBar</c>,
///         <c>NativeToolbar</c>) when you want platform-exact chrome — segmented titles, overflow menus,
///         per-bar tinting. That is the escape hatch, and a screen using it no longer compiles for the web
///         heads; the portable bars are the subset that does.
///     </para>
/// </summary>
public abstract partial class Screen : Component, IScreenChrome
{
    /// <summary>
    ///     The header bar for this screen, e.g. <c>AppBar.Title("Todos")</c>, or <c>null</c> for none. Typed as
    ///     <see cref="Component" /> so a screen can equally hand it the platform-exact
    ///     <c>NativeHeaderBar</c> — <c>Rask.Core</c> takes no dependency on the native package, and the native
    ///     host recognizes whichever bar it is handed.
    /// </summary>
    protected virtual Component? HeaderBar => null;

    /// <summary>
    ///     The toolbar for this screen (e.g. <c>NativeToolbar(...)</c>), or <c>null</c> for none. Contextual
    ///     actions are platform-exact only for now — there is no portable counterpart yet.
    /// </summary>
    protected virtual Component? Toolbar => null;

    /// <summary>
    ///     The tab bar, e.g. <c>TabStrip.Tabs([...])</c>, or <c>null</c> for none. Usually declared once on a
    ///     layout screen rather than per leaf — chrome merges deepest-wins per kind, so the leaf's
    ///     <see cref="HeaderBar" /> and the layout's tab bar coexist.
    /// </summary>
    protected virtual Component? TabBar => null;

    // Explicit IScreenChrome implementation: the serializer (in Rask.Core, which names no chrome type) reads
    // the slots through this interface, while they stay `protected` on the public surface — a screen's chrome
    // is declared by overriding, not by anyone calling it.
    Component? IScreenChrome.HeaderBarSlot => HeaderBar;

    Component? IScreenChrome.ToolbarSlot => Toolbar;

    Component? IScreenChrome.TabBarSlot => TabBar;
}
