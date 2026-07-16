namespace Rask.Example.Shared.Features;

// A Bootstrap offcanvas drawer driven entirely by Rask's live runtime — no bootstrap.js. _open is a
// plain field; the trigger sets it true, and OnClose (the ×, the backdrop click, or the footer button)
// sets it false. The panel stays in the DOM and slides in via the .show class the live diff toggles.
public sealed class BsOffcanvasDemo : Component
{
    private bool _open;

    protected override Component? Render() =>
    [
        BsButton(Color: BsColor.Primary, OnClick: () => _open = true)["Open settings"],
        BsOffcanvas(
            Open: _open,
            Title: "Settings",
            Placement: BsPlacement.End,
            OnClose: () => _open = false)[
            P()["This drawer slides in from the end over a dimming backdrop, all through the live diff. "
                + "Click the backdrop or the × to dismiss it — no bootstrap.js."],
            BsButton(Color: BsColor.Secondary, OnClick: () => _open = false)["Done"]
        ]
    ];
}
