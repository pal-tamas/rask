namespace Rask.Example.Shared.Features;

// A Bootstrap modal driven entirely by Rask's live runtime — no bootstrap.js. _open is a plain field;
// the trigger sets it true, OnClose (× button, backdrop click, or the footer button) sets it false.
public sealed partial class BsModalDemo : Component
{
    private bool _open;
    private bool _fullscreenOpen;

    protected override Component? Render() =>
    [
        Div(Class: Bs.Join(Display.Flex(), Flex.Gap(2)))[
            BsButton(Color: BsColor.Primary, OnClick: () => _open = true)["Launch demo modal"],
            BsButton(Color: BsColor.Secondary, OnClick: () => _fullscreenOpen = true)["Full-screen on phones"]
        ],
        BsModal(
            Open: _open,
            Title: "Zero-JS modal",
            Centered: true,
            OnClose: () => _open = false,
            Footer: BsButton(Color: BsColor.Secondary, OnClick: () => _open = false)["Close"])[
            P()["This modal — backdrop, show animation and click-outside-to-close — runs without "
                + "bootstrap.js. State lives in your component; Rask diffs the DOM."]
        ],
        // FullscreenBelow makes the dialog edge-to-edge below the breakpoint (great for forms on
        // phones) while staying a sized, centered dialog on larger screens.
        BsModal(
            Open: _fullscreenOpen,
            Title: "Full-screen below sm",
            Size: BsSize.Lg,
            FullscreenBelow: Bp.Sm,
            OnClose: () => _fullscreenOpen = false,
            Footer: BsButton(Color: BsColor.Secondary, OnClick: () => _fullscreenOpen = false)["Close"])[
            P()["Resize the window: below the sm breakpoint this dialog fills the screen edge-to-edge; "
                + "at sm and up it is a centered modal-lg. Set Fullscreen: true for full-screen at every width."]
        ]
    ];
}
