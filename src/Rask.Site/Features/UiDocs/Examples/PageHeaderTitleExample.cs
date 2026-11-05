namespace Rask.Site.Features.UiDocs.Examples;

// A form's page: the title alone, nothing at the end of the row.
public sealed partial class PageHeaderTitleExample : Component
{
    protected override Component? Render() => Ui.PageHeader.Heading("New supplier").Level(2).Class("mb-0");
}
