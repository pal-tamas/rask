namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class FieldSplitExample : Component
{
    protected override Component? Render() =>
        Div.Class("grid grid-cols-2 gap-4")[
            Ui.Input.Of<string>().Label("First name").Placeholder("Mara"),
            Ui.Input.Of<string>().Label("Last name").Placeholder("Lind")
        ];
}
