namespace Rask.Site.Features.UiDocs.Examples;

// A claim's page: its status stands on the title's line, after the h1 and centred with it. On a phone, once the title
// fills the line, the badge goes under it.
public sealed partial class PageHeaderBesideExample : Component
{
    protected override Component? Render() =>
        Div.Class("p-6 lg:p-8")[
            Ui.PageHeader.Heading("Diesel refund claim").Beside(Ui.Badge.Green["Submitted"])
                .Subheading("Filed on 12 November for the third quarter.").Class("mb-8"),
            Div.Class("grid max-w-sm gap-6")[
                Ui.Input.Value("1 240 l").Label("Fuel used"),
                Ui.Input.Value("Q3 2026").Label("Period")
            ]
        ];
}
