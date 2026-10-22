namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class InputMaskExample : Component
{
    protected override Component? Render() =>
        Ui.Input.Value("7161234567").Mask("(999) 999-9999").Attributes(("aria-label", "Phone")).Class("max-w-sm");
}
