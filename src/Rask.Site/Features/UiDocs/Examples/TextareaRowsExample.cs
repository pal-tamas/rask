namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TextareaRowsExample : Component
{
    protected override Component? Render() =>
        Ui.Textarea.Of<string>().Rows(2).Label("Note").Class("max-w-sm");
}
