namespace Rask.Site.Features.UiDocs.Examples;

// A form's page: the title alone, nothing at the end of the row, and the form 32px under it — the page's choice.
public sealed partial class PageHeaderTitleExample : Component
{
    protected override Component? Render() =>
        Div.Class("p-6 lg:p-8")[
            Ui.PageHeader.Heading("New supplier").Class("mb-8"),
            Div.Class("grid max-w-sm gap-6")[
                Ui.Input.Label("Name").Placeholder("Aminoff Freight"),
                Ui.Input.Label("City").Placeholder("Oslo")
            ]
        ];
}
