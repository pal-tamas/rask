namespace Rask.Core;

/// <summary>
///     A <see cref="Page" /> that also declares the native chrome around it — the header bar, toolbar and tab
///     bar a native host projects to real platform widgets (a <c>UINavigationBar</c>/<c>UITabBar</c> on iOS, a
///     top/bottom bar on Android).
///     <code>
/// public sealed class TodosScreen : Screen
/// {
///     protected override string Route => "/todos";
///
///     protected override Component? HeaderBar => NativeHeaderBar(Title: "Todos");
///
///     protected override Component? Render() => Div()[/* the HTML body */];
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
///         button's <c>OnClick</c> attributes back to the screen and re-renders it like any callback — but a
///         native bar emits no HTML, so nothing leaks into the WebView markup. This is what lets a screen own
///         its chrome instead of the app root inspecting the current path to decide what the header should say.
///     </para>
///     <para>
///         Chrome merges by kind, deepest-wins, so composition falls out of the route chain: a layout screen
///         (a <see cref="Page.Parent" />) supplies the <see cref="TabBar" /> once and each leaf screen supplies
///         its own <see cref="HeaderBar" />.
///     </para>
///     <para>
///         On Server and WASM the slots are <b>never read</b> — those hosts don't collect chrome — so one
///         screen class serves web and native with no <c>IsNative</c> branching, and a web-only app pays
///         nothing for the base class.
///     </para>
/// </summary>
public abstract class Screen : Page
{
    /// <summary>
    ///     The native header bar for this screen, e.g. <c>NativeHeaderBar(Title: "Todos")</c>, or <c>null</c>
    ///     for none. Typed as <see cref="Component" /> so <c>Rask.Core</c> takes no dependency on the native
    ///     package — the native host recognizes the bar it is handed.
    /// </summary>
    protected virtual Component? HeaderBar => null;

    /// <summary>The native toolbar for this screen (e.g. <c>NativeToolbar(...)</c>), or <c>null</c> for none.</summary>
    protected virtual Component? Toolbar => null;

    /// <summary>
    ///     The native tab bar, or <c>null</c> for none. Usually declared once on a layout screen rather than
    ///     per leaf — chrome merges deepest-wins per kind, so the leaf's <see cref="HeaderBar" /> and the
    ///     layout's tab bar coexist.
    /// </summary>
    protected virtual Component? TabBar => null;

    // Protected members aren't reachable from a sibling class in the same assembly, so the serializer reads
    // the slots through these — the same pattern Head uses (HeadInternal).
    internal Component? HeaderBarInternal => HeaderBar;

    internal Component? ToolbarInternal => Toolbar;

    internal Component? TabBarInternal => TabBar;
}
