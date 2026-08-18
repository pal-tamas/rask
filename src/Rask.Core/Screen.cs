namespace Rask.Core;

/// <summary>
///     A <see cref="Page" /> that also declares the chrome around it — the header bar, toolbar and tab bar.
///     Fill the slots with the portable <c>Rask.Core</c> bars (<see cref="Components.AppBar" /> /
///     <see cref="Components.TabStrip" />) and one screen class serves every host: the web hosts render
///     landmark markup, and a native host projects the same declaration to real platform widgets (a
///     <c>UINavigationBar</c>/<c>UITabBar</c> on iOS, a top/bottom bar on Android).
///     <code>
/// public sealed class TodosScreen : Screen
/// {
///     protected override string Route => "/todos";
///
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
///         (a <see cref="Page.Parent" />) supplies the <see cref="TabBar" /> once and each leaf screen supplies
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
public abstract class Screen : Page
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

    // Protected members aren't reachable from a sibling class in the same assembly, so the serializer reads
    // the slots through these — the same pattern Head uses (HeadInternal).
    internal Component? HeaderBarInternal => HeaderBar;

    internal Component? ToolbarInternal => Toolbar;

    internal Component? TabBarInternal => TabBar;
}
