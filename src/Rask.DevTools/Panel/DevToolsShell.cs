using Rask.Core;
using Rask.Core.Components;

namespace Rask.DevTools.Panel;

/// <summary>
///     The root of the devtools panel: the application mounted at <c>/_rask-devtools</c>, framed by the drawer the
///     host script opens on a Debug page in Development.
/// </summary>
/// <remarks>
///     It renders the router and nothing else, like the operator console's shell: the chrome is
///     <see cref="DevToolsLayout" />'s. Who may reach it is decided before it renders, by the Server host's admission
///     check. What it adds is <see cref="DevToolsPanelRoot" />, which the layout requires, so the panel's pages render
///     only under a root that check chose.
/// </remarks>
internal sealed partial class DevToolsShell : Component
{
    /// <summary>Document-level head content; the layout contributes the title and the stylesheet.</summary>
    protected override Component? HeadAssets =>
    [
        Meta.Charset("utf-8"),
        Meta.Name("viewport").Content("width=device-width, initial-scale=1"),
        // Dark mode as Flux has it: the app's own appearance (same origin, same key), the system's until one is
        // picked — so the panel matches the page it is docked to, as a browser's own devtools follow its setting.
        Ui.AppearanceScript,
        // A developer tool has no business in a search index.
        Meta.Name("robots").Content("noindex, nofollow"),
    ];

    /// <summary>
    ///     A document drawn with the kit alone: its reset and its ground (<see cref="UiStylesheet.DocumentAttribute" />).
    /// </summary>
    protected override Component Shell(Component head, Component body) =>
        Html.Lang(HtmlLang).Dir(HtmlDir).Attributes((UiStylesheet.DocumentAttribute, ""))[
            head,
            Body.Class(BodyClass)[body]
        ];

    /// <inheritdoc />
    protected override Component? Render() => Context.Provide(DevToolsPanelRoot.Instance)[Router];
}
