namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CheckboxCardIconsExample : Component
{
    private ICollection<string> Subscription { get; set; } = ["newsletter"];

    protected override Component? Render() =>
        Ui.CheckboxGroup.Bind(() => Subscription).Label("Subscription preferences").Cards.Class("max-w-md flex-col")[
            Ui.Checkbox.Value("newsletter").Icon(Ui.IconName.Newspaper).Label("Newsletter")
                .Description("Get the latest updates and offers."),
            Ui.Checkbox.Value("updates").Icon(Ui.IconName.Cube).Label("Product updates")
                .Description("Learn about new features and products."),
            Ui.Checkbox.Value("invitations").Icon(Ui.IconName.Calendar).Label("Event invitations")
                .Description("Invitations to exclusive events.")
        ];
}
