namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class InputGroupLabelExample : Component
{
    private string Website { get; set; } = "";

    protected override Component? Render() =>
        Ui.Field.Class("max-w-sm")[
            Ui.Label["Website"],
            Ui.InputGroup[
                Ui.InputGroupPrefix["https://"],
                Ui.Input.Bind(() => Website).Placeholder("example.com")
            ],
            Ui.Error
        ];
}
