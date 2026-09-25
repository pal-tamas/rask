using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Routing;

namespace Rask.DevTools.Panel;

/// <summary>
///     The panel's chrome, and the one place its stylesheet is contributed.
/// </summary>
/// <remarks>
///     The kit's stylesheet is inlined rather than linked, for the operator console's reason: the panel is mounted into
///     an app that may not reference Rask.Ui itself, so no build ever wrote the sheet a <c>&lt;link&gt;</c> would point
///     at. Panel markup uses kit components only, so the kit's sheet is the whole of it.
/// </remarks>
[Route("_rask-devtools")]
internal sealed partial class DevToolsLayout : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets =>
    [
        Title["Rask DevTools"],
        // Raw, because CSS is not HTML: encoding it would break every selector containing > or &.
        Style[Raw.Value(UiStylesheet.Css)],
    ];

    /// <inheritdoc />
    protected override Component? Render()
    {
        // A second line, behind the host's: a live app session that navigates here over its socket is sent to load the
        // panel as a page (#1094), so the panel's admission runs. Should anything still render this layout outside the
        // panel's own shell, which alone provides the root marker, it shows nothing of the session it names.
        if (!Context.Has<DevToolsPanelRoot>())
        {
            return Ui.Alert["The Rask DevTools panel opens only in its own frame. Open it from the page's Rask pill."];
        }

        return Ui.Shell[
            Ui.TopBar[Ui.Brand.Label("Rask DevTools").Href("#")],
            Ui.Main[Outlet]
        ];
    }
}
