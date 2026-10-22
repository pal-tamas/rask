namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TextareaAutoExample : Component
{
    protected override Component? Render() =>
        Ui.Textarea.Of<string>().Rows(UiTextareaRows.Auto).Placeholder("This textarea will adjust to fit the content...")
            .Class("max-w-sm");
}
