namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CheckboxDescriptionsExample : Component
{
    private ICollection<string> Subscription { get; set; } = ["newsletter"];

    protected override Component? Render() =>
        Ui.CheckboxGroup.Bind(() => Subscription).Label("Subscription preferences")[
            Ui.Checkbox.Value("newsletter").Label("Newsletter")
                .Description("Receive our monthly newsletter with the latest updates and offers."),
            Ui.Checkbox.Value("updates").Label("Product updates")
                .Description("Stay informed about new features and product updates."),
            Ui.Checkbox.Value("invitations").Label("Event invitations")
                .Description("Get invitations to our exclusive events and webinars.")
        ];
}
