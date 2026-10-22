namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class InputExample : Component
{
    private string Username { get; set; } = "";

    protected override Component? Render() =>
        Ui.Field.Class("max-w-sm")[
            Ui.Label["Username"],
            Ui.Description["This will be publicly displayed."],
            Ui.Input.Bind(() => Username),
            Ui.Error
        ];
}
