namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SliderCustomStepsExample : Component
{
    private int _level = 3;

    protected override Component? Render() =>
        Ui.Slider.Value(_level).Min(1).Max(5).OnChange(level => { _level = level; }).Class("max-w-sm")[
            Ui.SliderTick.Value(1)["Low"],
            Ui.SliderTick.Value(3)["Mid"],
            Ui.SliderTick.Value(5)["High"]
        ];
}
