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
/// <c>noindex</c> and the stylesheet links — comes from <c>DashboardLayout</c>, which is where it belongs:
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
    /// the <c>noindex</c> and the stylesheets; these two are document-level and belong to whatever is
    /// serving the document, which here is this shell.
    /// </summary>
    protected override Component? HeadAssets =>
    [
        Meta.Charset("utf-8"),
        Meta.Name("viewport").Content("width=device-width, initial-scale=1"),
    ];

    /// <inheritdoc />
    protected override Component? Render() => Router;
}
