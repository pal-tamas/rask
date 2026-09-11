namespace Rask.Dashboard;

/// <summary>
/// A root component for a host that serves the dashboard and nothing else of its own.
/// <para>
/// A <c>server</c>-template app never needs this: its own <c>App</c> is already the root, it already
/// renders the router, and the dashboard's pages join that app's route chain through the module
/// initializer <c>Rask.Dashboard</c> ships. A host serving a single-page app is the case this exists for —
/// a WebAssembly app or a TypeScript front end served by <c>UseRaskSpa</c>, where the ASP.NET host runs no
/// components at all, and mounting the operator dashboard on it means naming a root for
/// <c>UseRaskServer&lt;TApp&gt;</c> to render. Without this, every such host would hand-roll the same four
/// lines.
/// </para>
/// <para>
/// It renders the router and nothing else. Everything visible — the navbar, the panels, the
/// <c>noindex</c> and the stylesheet links — comes from <c>DashboardLayout</c>, which is where it belongs:
/// this type must not become a second place the dashboard's chrome is decided.
/// </para>
/// <example>
/// In a host serving a single-page app, mounted under the dashboard's own prefix so the SPA fallback
/// keeps every other route:
/// <code>
/// app.UseRaskServer&lt;RaskDashboardShell&gt;("/_rask/{**path}");
/// app.UseRaskSpa();
/// </code>
/// </example>
/// </summary>
public sealed partial class RaskDashboardShell : Component
{
    /// <summary>
    /// Only what the dashboard's own layout cannot contribute. <c>DashboardLayout</c> supplies the title,
    /// the <c>noindex</c> and the stylesheets; these two are document-level and belong to whatever is
    /// serving the document, which here is this shell.
    /// </summary>
    protected override Component? HeadAssets =>
    [
        Meta.Charset("utf-8"),
        Meta.Name("viewport").Content("width=device-width, initial-scale=1"),
    ];

    /// <summary>
    /// Puts the kit's theme scope, pinned to daisyUI's light theme, on <c>&lt;html&gt;</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not chrome, and so not the layout's: this is the document element, and the layout renders inside
    /// the body. Two separate things ride on it.
    /// </para>
    /// <para>
    /// <b>The scope has to reach <c>:root</c>.</b> The console's stylesheet expresses every
    /// <c>--color-ui-*</c> token as an alias for one of daisyUI's semantic variables, and Tailwind emits
    /// that <c>@theme</c> block at <c>:root</c>. A custom property's <c>var()</c> is substituted where the
    /// declaration applies, so <c>--color-base-100</c> has to be defined at <c>:root</c> as well or every
    /// alias computes to nothing and inherits nothing — a fully laid-out console with no colour in it.
    /// <c>UiShell</c> carrying the scope on a div inside the body is not enough for that.
    /// </para>
    /// <para>
    /// <b>And the theme has to be named.</b> daisyUI follows <c>prefers-color-scheme</c> through
    /// <c>[data-rask-ui]:not([data-theme])</c>, so a scope with no theme repaints the console dark on an
    /// operator's dark-mode laptop while the contrast ratios its stylesheet is built on are all measured
    /// against a white ground. An operator surface is not the host's, nor the OS's, to re-theme.
    /// </para>
    /// </remarks>
    protected override Component Shell(Component head, Component body) =>
        Html.Lang(HtmlLang).Dir(HtmlDir)
            .Attributes(
                (UiStylesheet.ThemeScopeAttribute, ""),
                ("data-theme", UiTheme.Value(DashboardTheme.Name)))[
            head,
            Body.Class(BodyClass)[body]
        ];

    /// <inheritdoc />
    protected override Component? Render() => Router;
}
