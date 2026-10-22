namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CheckboxCardsExample : Component
{
    private ICollection<string> Subscription { get; set; } = ["newsletter"];

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Ui.CheckboxGroup.Bind(() => Subscription).Live().Id("cb-cards").Label("Subscription preferences").Cards
                .Class("max-sm:flex-col")[
                Ui.Checkbox.Value("newsletter").Label("Newsletter").Description("Get the latest updates and offers."),
                Ui.Checkbox.Value("updates").Label("Product updates").Description("Learn about new features and products."),
                Ui.Checkbox.Value("invitations").Label("Event invitations").Description("Invitations to exclusive events.")
            ],
            Ui.Text.Data("testid", "ui-checkbox-cards-state")[$"Subscribed to {string.Join(", ", Subscription)}."]
        ];
}
