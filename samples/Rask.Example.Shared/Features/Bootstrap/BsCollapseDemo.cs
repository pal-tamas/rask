namespace Rask.Example.Shared.Features;

// A Bootstrap collapse driven entirely by Rask's live runtime — no bootstrap.js. _open is a plain
// field; the toggle button flips it and the .show class that reveals the panel is added/removed by the
// live diff. The button label tracks the state so the control reads correctly for assistive tech.
public sealed partial class BsCollapseDemo : Component
{
    private bool _open;

    protected override Component? Render() =>
    [
        BsButton.Color(BsColor.Primary).OnClick(() => _open = !_open)[
            _open ? "Hide details" : "Show details"
        ],
        BsCollapse.Open(_open)[
            Div.Class("card card-body mt-2")[
                "This panel is revealed by toggling Open — Rask adds the .show class through the live "
                + "diff, with no bootstrap.js and no manual DOM work."
            ]
        ]
    ];
}
