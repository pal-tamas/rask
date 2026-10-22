namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SliderMarksExample : Component
{
    private int _step = 3;

    protected override Component? Render() =>
        Ui.Slider.Value(_step).Min(1).Max(5).OnChange(step => { _step = step; }).Class("max-w-sm")[
            Enumerable.Range(1, 5).Select(step => Ui.SliderTick.Key(step).Value(step))
        ];
}
