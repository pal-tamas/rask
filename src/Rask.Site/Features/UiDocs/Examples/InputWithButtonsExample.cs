namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class InputWithButtonsExample : Component
{
    private string? _currency = "USD";

    protected override Component? Render() =>
        Div.Class("grid max-w-sm gap-4")[
            Ui.InputGroup[
                Ui.Input.Of<string>().Placeholder("Post title"),
                Ui.Button.Icon(Ui.IconName.Plus)["New post"]
            ],
            Ui.InputGroup[
                Ui.Select.Value(_currency).OnChange(currency => { _currency = currency; }).Class("max-w-fit")[
                    Ui.SelectOption["USD"],
                    Ui.SelectOption["EUR"],
                    Ui.SelectOption["GBP"]
                ],
                Ui.Input.Of<string>().Placeholder("$99.99")
            ]
        ];
}
