using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Routing;
using Rask.Ui;

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
        // Every router matches every mounted application's pages, so a live app session can navigate itself here over
        // its own socket, past the admission its page handler runs. Only the panel's own shell provides the root marker;
        // anywhere else the panel shows nothing of the session it names.
        if (!Context.Has<DevToolsPanelRoot>())
        {
            return UiAlert["The Rask DevTools panel opens only in its own frame. Open it from the page's Rask pill."];
        }

        return UiShell[
            UiTopBar[UiBrand.Label("Rask DevTools").Href("#")],
            UiMain[Outlet]
        ];
    }
}
