namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TextareaResizeExample : Component
{
    protected override Component? Render() =>
        Div.Class("grid max-w-sm gap-4")[
            Ui.Textarea.Of<string>().Vertical.Placeholder("Resize \"vertical\""),
            Ui.Textarea.Of<string>().None.Placeholder("Resize \"none\""),
            Ui.Textarea.Of<string>().Horizontal.Placeholder("Resize \"horizontal\""),
            Ui.Textarea.Of<string>().Both.Placeholder("Resize \"both\"")
        ];
}
