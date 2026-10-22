namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SliderMinStepsExample : Component
{
    private int[] _range = [40, 60];

    protected override Component? Render() =>
        Ui.Slider.Value(_range).Range().Step(1).MinStepsBetween(10).OnChange(range => { _range = range; }).Class("max-w-sm");
}
