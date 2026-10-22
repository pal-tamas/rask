namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class InputAffixesExample : Component
{
    protected override Component? Render() =>
        Div.Class("grid max-w-sm gap-4")[
            Ui.InputGroup[
                Ui.InputGroupPrefix["https://"],
                Ui.Input.Of<string>().Placeholder("example.com")
            ],
            Ui.InputGroup[
                Ui.Input.Of<string>().Placeholder("chunky-spaceship"),
                Ui.InputGroupSuffix[".brand.com"]
            ]
        ];
}
