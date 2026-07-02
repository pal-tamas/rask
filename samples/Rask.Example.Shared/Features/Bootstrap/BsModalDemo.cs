namespace Rask.Example.Shared.Features;

// A Bootstrap modal driven entirely by Rask's live runtime — no bootstrap.js. _open is a plain field;
// the trigger sets it true, OnClose (× button, backdrop click, or the footer button) sets it false.
public sealed class BsModalDemo : Component
{
    private bool _open;

    protected override Component? Render() =>
    [
        BsButton(Color: BsColor.Primary, OnClick: () => _open = true)["Launch demo modal"],
        BsModal(
            Open: _open,
            Title: "Zero-JS modal",
            Centered: true,
            OnClose: () => _open = false,
            Footer: BsButton(Color: BsColor.Secondary, OnClick: () => _open = false)["Close"])[
            P()["This modal — backdrop, show animation and click-outside-to-close — runs without "
                + "bootstrap.js. State lives in your component; Rask diffs the DOM."]
        ]
    ];
}
