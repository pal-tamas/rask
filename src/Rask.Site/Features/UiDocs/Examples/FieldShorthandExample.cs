namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class FieldShorthandExample : Component
{
    private string Email { get; set; } = "";

    protected override Component? Render() =>
        Ui.Input.Bind(() => Email).Label("Email").Type(InputType.Email).Class("max-w-sm");
}
