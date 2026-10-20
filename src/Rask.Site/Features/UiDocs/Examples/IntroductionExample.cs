namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class IntroductionExample : Component
{
    private string? _name = "Ada";
    private int _saves;

    protected override Component? Render() =>
        Ui.Card.Class("max-w-md space-y-6")[
            Div[
                Ui.Heading.Level(3).Lg["Update profile"],
                Ui.Text.Class("mt-2")["Make changes to your personal details."]
            ],
            Ui.Input.Value(_name).OnChange(name => _name = name).Label("Name").Placeholder("Your name"),
            Div.Class("flex items-center gap-2")[
                Ui.Text.Sm.Subtle[_saves == 0 ? "Not saved yet." : $"Saved {_saves}× as {_name}."],
                Ui.Spacer,
                Ui.Button.Ghost.OnClick(() => { _name = ""; })["Clear"],
                Ui.Button.Primary.OnClick(() => { _saves++; })["Save"]
            ]
        ];
}
