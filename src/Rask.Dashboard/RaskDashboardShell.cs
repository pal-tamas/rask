namespace Rask.Dashboard;

/// <summary>
/// A root component for a host that serves the dashboard and nothing else of its own.
/// <para>
/// A <c>server</c>-template app never needs this: its own <c>App</c> is already the root, it already
/// renders the router, and the dashboard's pages join that app's route chain through the module
/// initializer <c>Rask.Dashboard</c> ships. The <c>wasm-hosted</c> template is the case this exists for —
/// there the UI is a browser-WASM SPA, the ASP.NET host runs no components at all, and mounting the
/// operator dashboard on it means naming a root for <c>UseRaskServer&lt;TApp&gt;</c> to render. Without
/// this, every such host would hand-roll the same four lines.
/// </para>
/// <para>
/// It renders the router and nothing else. Everything visible — the navbar, the panels, the
/// <c>noindex</c> and the stylesheet — comes from <c>DashboardLayout</c>, which is where it belongs:
/// this type must not become a second place the dashboard's chrome is decided.
/// </para>
/// <example>
/// In a wasm-hosted <c>.Server</c> host, mounted under the dashboard's own prefix so the SPA fallback
/// keeps every other route:
/// <code>
/// app.UseRaskServer&lt;RaskDashboardShell&gt;("/_rask/{**path}");
/// app.UseRaskWasmHost();
/// </code>
/// </example>
/// </summary>
public sealed partial class RaskDashboardShell : Component
{
    /// <summary>
    /// Only what the dashboard's own layout cannot contribute. <c>DashboardLayout</c> supplies the title,
    /// the <c>noindex</c> and the stylesheet; these two are document-level and belong to whatever is
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
    /// <b>The scope has to reach the document.</b> daisyUI defines <c>--color-base-*</c> only inside
    /// <c>[data-rask-ui]</c>, and the kit's console reset paints <c>&lt;body&gt;</c> from
    /// <c>--color-base-200</c> — the page ground an operator sees past the frame on a wide screen and while
    /// scrolling past its end. A custom property is inherited downward, never up, so <c>UiShell</c> carrying
    /// the scope on a div inside the body cannot give the body that colour.
    /// </para>
    /// <para>
    /// <b>And the theme has to be named.</b> daisyUI follows <c>prefers-color-scheme</c> through
    /// <c>[data-rask-ui]:not([data-theme])</c>, so a scope with no theme repaints the console dark on an
    /// operator's dark-mode laptop. An operator surface is not the host's, nor the OS's, to re-theme.
    /// </para>
    /// </remarks>
    protected override Component Shell(Component head, Component body) =>
        Html.Lang(HtmlLang).Dir(HtmlDir)
            .Attributes(
                (UiStylesheet.ThemeScopeAttribute, ""),
                ("data-theme", UiTheme.Value(DashboardTheme.Name)))[
            head,
            Body[body]
        ];

    /// <inheritdoc />
    protected override Component? Render() => Router;
}
