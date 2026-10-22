namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SliderStepExample : Component
{
    private int _amount = 50;

    protected override Component? Render() =>
        Ui.Slider.Value(_amount).Min(0).Max(100).Step(10).OnChange(amount => { _amount = amount; }).Class("max-w-sm");
}
