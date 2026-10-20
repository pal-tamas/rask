namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CalloutDismissibleExample : Component
{
    private bool _meeting = true;
    private bool _login = true;

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            _meeting
                ? Ui.Callout.Key("meeting").Icon(Ui.IconName.Bell).Secondary.Inline().Controls(Dismiss(() => _meeting = false))[
                    Ui.CalloutHeading["Upcoming meeting"],
                    Ui.CalloutText["10:00 AM"]
                ]
                : null,
            _login
                ? Ui.Callout.Key("login").Icon(Ui.IconName.FingerPrint).Secondary
                    .Actions([Ui.Button["Change password"], Ui.Button.Ghost["Review activity"]])
                    .Controls(Dismiss(() => _login = false))[
                    Ui.CalloutHeading["Unusual login attempt"],
                    Ui.CalloutText["We detected a login from a new device in New York, USA. If this was you, no action is needed. If not, secure your account immediately."]
                ]
                : null,
            _meeting && _login ? null : Ui.Button.Key("restore").Sm.OnClick(() => { _meeting = _login = true; })["Show them again"]
        ];

    private static UiButton Dismiss(Action dismiss) =>
        Ui.Button.Ghost.Icon(Ui.IconName.XMark).AriaLabel("Dismiss").OnClick(dismiss);
}
