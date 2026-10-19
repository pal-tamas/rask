namespace Rask.Dashboard;

/// <summary>
/// A root component for a host that serves the dashboard and nothing else of its own.
/// <para>
/// A <c>server</c>-template app never needs this: its own <c>App</c> is already the root, it already
/// renders the router, and <c>AddRaskDashboard</c> mounts the console beside it. A host serving a single-page app is the case this exists for —
/// a WebAssembly app served by <c>MapRaskSpa</c>, where the ASP.NET host runs no
/// components at all, and mounting the operator dashboard on it means naming a root for
/// <c>MapRaskServer&lt;TApp&gt;</c> to render. Without this, every such host would hand-roll the same four
/// lines.
/// </para>
/// <para>
/// It renders the router and nothing else. Everything visible — the navbar, the panels, the
/// <c>noindex</c> and the stylesheet — comes from <c>DashboardLayout</c>, which is where it belongs:
/// this type must not become a second place the dashboard's chrome is decided.
/// </para>
/// <example>
/// In a host serving a single-page app, mounted under the dashboard's own prefix so the SPA fallback
/// keeps every other route:
/// <code>
/// app.MapRaskServer&lt;RaskDashboardShell&gt;("/_rask/{**path}");
/// app.MapRaskSpa();
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
    /// Marks <c>&lt;html&gt;</c> as a document drawn with the kit alone.
    /// </summary>
    /// <remarks>
    /// Not chrome, and so not the layout's: this is the document element, and the layout renders inside the body.
    /// <see cref="UiStylesheet.DocumentAttribute" /> is what gives a kit-only document its reset and its ground, light
    /// and dark. Dark is Flux's: a <c>dark</c> class on this element, which <c>Ui.AppearanceScript</c> in the layout's
    /// head puts there from the operator's own preference before the first paint.
    /// </remarks>
    protected override Component Shell(Component head, Component body) =>
        Html.Lang(HtmlLang).Dir(HtmlDir).Attributes((UiStylesheet.DocumentAttribute, ""))[
            head,
            Body[body]
        ];

    /// <inheritdoc />
    protected override Component? Render() => Router;
}
