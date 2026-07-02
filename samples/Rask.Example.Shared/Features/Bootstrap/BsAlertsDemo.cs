namespace Rask.Example.Shared.Features;

// Bootstrap alerts, including a dismissible one — dismissal is driven by Rask's live runtime (the
// OnClose handler flips a field), with no bootstrap.js.
public sealed class BsAlertsDemo : Component
{
    private bool _show = true;

    protected override Component? Render() =>
    [
        Div(Class: "vstack gap-2")[
            BsAlert(Color: BsColor.Primary)["A simple primary alert — check it out!"],
            BsAlert(Color: BsColor.Success)[
                BsIcon(Name: BsIconName.CheckCircle, Class: "me-2"), "Well done! You read this message."
            ],
            _show
                ? BsAlert(Color: BsColor.Warning, Dismissible: true, OnClose: () => _show = false)[
                    "Dismiss me — zero JavaScript, the close button flips component state."
                  ]
                : BsButton(Color: BsColor.Secondary, Size: BsSize.Sm, OnClick: () => _show = true)["Restore alert"]
        ]
    ];
}
