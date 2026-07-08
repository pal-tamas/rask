namespace Rask.Example.Shared.Features;

// BsDropdown is controlled (zero-JS): _open is a plain field, OnToggle flips it, and each BsDropdownItem's
// OnClick sets it back to false on selection. The menu is a Popper-less .dropdown-menu — Rask re-anchors
// it with position:fixed while open, so it escapes this overflow:hidden sample card instead of being
// clipped. The right-aligned variant shows AlignEnd working without Popper.
public sealed class BsDropdownDemo : Component
{
    private bool _open;
    private bool _alignOpen;
    private string _picked = "—";

    protected override Component? Render() =>
    [
        Div(Class: Bs.Join(Display.Flex(), Flex.Gap(3), Flex.Wrap()))[
            BsDropdown(Id: "demo-dropdown", Label: "Actions", Color: BsColor.Primary,
                Open: _open, OnToggle: () => _open = !_open)[
                BsDropdownItem(Header: true)["Manage"],
                BsDropdownItem(OnClick: () => Pick("Edit"))["Edit"],
                BsDropdownItem(OnClick: () => Pick("Duplicate"))["Duplicate"],
                BsDropdownItem(Divider: true),
                BsDropdownItem(OnClick: () => Pick("Archive"))["Archive"]
            ],
            BsDropdown(Id: "demo-dropdown-end", Label: "Right-aligned", Color: BsColor.Secondary,
                AlignEnd: true, Open: _alignOpen, OnToggle: () => _alignOpen = !_alignOpen)[
                BsDropdownItem(OnClick: () => Pick("Share"))["Share"],
                BsDropdownItem(OnClick: () => Pick("Export"))["Export"]
            ]
        ],
        BsAlert(Color: BsColor.Info, Class: "mt-3 mb-0")[
            Span(Id: "demo-dropdown-out")["Last action: ", Strong()[_picked]]
        ]
    ];

    private void Pick(string action)
    {
        _picked = action;
        _open = false;
        _alignOpen = false;
    }
}
