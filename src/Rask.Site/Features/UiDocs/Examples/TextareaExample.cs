namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TextareaExample : Component
{
    protected override Component? Render() =>
        Ui.Textarea.Of<string>().Class("max-w-sm");
}
