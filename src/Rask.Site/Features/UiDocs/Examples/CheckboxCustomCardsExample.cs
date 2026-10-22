namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CheckboxCustomCardsExample : Component
{
    private ICollection<string> Subscription { get; set; } = ["newsletter"];

    protected override Component? Render() =>
        Ui.CheckboxGroup.Bind(() => Subscription).Label("Subscription preferences").Cards.Class("max-w-md flex-col")[
            Ui.Checkbox.Value("newsletter")[
                Ui.CheckboxIndicator,
                Div.Class("flex-1")[
                    Ui.Heading.Class("leading-4")["Newsletter"],
                    Ui.Text.Sm.Class("mt-2")["Get the latest updates and offers."]
                ]
            ],
            Ui.Checkbox.Value("updates")[
                Ui.CheckboxIndicator,
                Div.Class("flex-1")[
                    Ui.Heading.Class("leading-4")["Product updates"],
                    Ui.Text.Sm.Class("mt-2")["Learn about new features and products."]
                ]
            ],
            Ui.Checkbox.Value("invitations")[
                Ui.CheckboxIndicator,
                Div.Class("flex-1")[
                    Ui.Heading.Class("leading-4")["Event invitations"],
                    Ui.Text.Sm.Class("mt-2")["Invitations to exclusive events."]
                ]
            ]
        ];
}
