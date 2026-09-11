using Rask.Core;
using Rask.Ui;

namespace Rask.DevTools.Panel;

/// <summary>
///     The root of the devtools panel: the application mounted at <c>/_rask-devtools</c>, framed by the drawer the
///     host script opens on a Debug page in Development.
/// </summary>
/// <remarks>
///     It renders the router and nothing else, like the operator console's shell: the chrome is
///     <see cref="DevToolsLayout" />'s. Who may reach it is decided before it renders, by the Server host's admission
///     check — never by anything here.
/// </remarks>
internal sealed partial class DevToolsShell : Component
{
    /// <summary>Document-level head content; the layout contributes the title and the stylesheet.</summary>
    protected override Component? HeadAssets =>
    [
        Meta.Charset("utf-8"),
        Meta.Name("viewport").Content("width=device-width, initial-scale=1"),
        // A developer tool has no business in a search index.
        Meta.Name("robots").Content("noindex, nofollow"),
    ];

    /// <summary>
    ///     The kit's theme scope on <c>&lt;html&gt;</c>, where its tokens resolve — with no theme named, so the panel
    ///     follows the developer's light or dark setting the way their browser's own devtools do.
    /// </summary>
    protected override Component Shell(Component head, Component body) =>
        Html.Lang(HtmlLang).Dir(HtmlDir).Attributes((UiStylesheet.ThemeScopeAttribute, ""))[
            head,
            Body.Class(BodyClass)[body]
        ];

    /// <inheritdoc />
    protected override Component? Render() => Router;
}
