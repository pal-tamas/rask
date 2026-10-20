namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ButtonLoadingExample : Component
{
    private int _saves;
    private int _steps;

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-3")[
            Ui.Button.Primary.OnClick(async () =>
            {
                await Task.Delay(1500);
                _saves++;
            })["Save changes"],
            Ui.Button.Icon(Ui.IconName.Plus).Loading(false).OnClick(async () =>
            {
                await Task.Delay(400);
                _steps++;
            })["Step"],
            Ui.Text.Sm.Inline()[$"Saved {_saves} time{(_saves == 1 ? "" : "s")} · stepped {_steps}"]
        ];
}
