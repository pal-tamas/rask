namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ModalConfirmationExample : Component
{
    private bool _deleted;

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-4")[
            Ui.ModalTrigger.Name("delete-profile")[Ui.Button.Danger["Delete"]],
            Ui.Modal.Name("delete-profile").Class("min-w-[22rem]")[
                Div.Class("space-y-6")[
                    Div[
                        Ui.Heading.Level(2).Lg["Delete project?"],
                        Ui.Text.Class("mt-2")["You're about to delete this project.", Br, "This action cannot be reversed."]
                    ],
                    Div.Class("flex gap-2")[
                        Ui.Spacer,
                        Ui.ModalClose.Key("cancel")[Ui.Button.Ghost["Cancel"]],
                        // A close, and the button's own handler still runs.
                        Ui.ModalClose.Key("delete")[
                            Ui.Button.Danger.OnClick(() => { _deleted = true; })["Delete project"]
                        ]
                    ]
                ]
            ],
            Ui.Text[_deleted ? "You deleted the project." : "The project is still there."]
        ];
}
