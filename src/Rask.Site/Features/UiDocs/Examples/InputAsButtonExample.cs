namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class InputAsButtonExample : Component
{
    private int _pressed;

    protected override Component? Render() =>
        Div.Class("grid max-w-sm gap-4")[
            Ui.Input.Of<string>().As(Ui.InputAs.Button).Placeholder("Search...").Icon(Ui.IconName.MagnifyingGlass).Kbd("⌘K")
                .OnClick(() => { _pressed++; }),
            Ui.Text[$"Pressed {_pressed} time{(_pressed == 1 ? "" : "s")}."]
        ];
}
