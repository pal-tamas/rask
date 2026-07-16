namespace Rask.Example.Shared.Features;

// Bootstrap spinners. Kind picks the border (default) or grow animation, Color themes it, and Small
// renders the compact variant. A visually-hidden "Loading…" label is emitted for screen readers unless
// you supply your own children.
public sealed class BsSpinnerDemo : Component
{
    protected override Component? Render() =>
        Div(Class: "hstack gap-3")[
            BsSpinner(Color: BsColor.Primary),
            BsSpinner(Kind: BsSpinnerKind.Grow, Color: BsColor.Secondary),
            BsSpinner(Color: BsColor.Success, Small: true),
            BsSpinner(Kind: BsSpinnerKind.Grow, Color: BsColor.Danger, Small: true)
        ];
}
