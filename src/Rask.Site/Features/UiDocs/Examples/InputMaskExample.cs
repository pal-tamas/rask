namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class InputMaskExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-6")[
            Ui.Input.Value("7161234567").Mask("(999) 999-9999").Attributes(("aria-label", "Phone")),
            Ui.Input.Value(1234.56m).Money().Attributes(("aria-label", "Amount"))
        ];
}
