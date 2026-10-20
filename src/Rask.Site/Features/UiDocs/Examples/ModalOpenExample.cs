namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ModalOpenExample : Component
{
    private bool _confirming;
    private string _last = "nothing yet";

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-4")[
            Ui.Button.Danger.OnClick(() => { _confirming = true; })["Delete order"],
            Ui.Modal
                .Open(_confirming)
                .OnCancel(() => { _last = "dismissed the dialog"; })
                .OnClose(() => { _confirming = false; })[
                Div.Class("space-y-6")[
                    Div[
                        Ui.Heading.Level(2).Lg["Delete order"],
                        Ui.Text.Class("mt-2")["This cannot be undone."]
                    ],
                    Div.Class("flex gap-2")[
                        Ui.Spacer,
                        Ui.ModalClose[Ui.Button.Ghost["Cancel"]],
                        Ui.Button.Danger.OnClick(() =>
                        {
                            _confirming = false;
                            _last = "deleted the order";
                        })["Delete"]
                    ]
                ]
            ],
            Ui.Text[$"Last action: {_last}."]
        ];
}
