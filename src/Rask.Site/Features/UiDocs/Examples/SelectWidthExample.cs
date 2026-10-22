namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectWidthExample : Component
{
    private string? _visibility;

    protected override Component? Render() =>
        Ui.Select.Value(_visibility).OnChange(value => { _visibility = value; }).Listbox.Placeholder("Select visibility...").Class("max-w-32").OptionsClass("min-w-72")[
            Ui.SelectOption.Value("public").Label("Public").Icon(Ui.IconName.GlobeAlt)
                .Description("Shown on your calendar and eligible to be featured on our homepage."),
            Ui.SelectOption.Value("private").Label("Private").Icon(Ui.IconName.LockClosed)
                .Description("Unlisted. Only invited people and people with the link can register.")
        ];
}
