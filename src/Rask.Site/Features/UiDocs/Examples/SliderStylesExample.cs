namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SliderStylesExample : Component
{
    private int _amount = 50;

    protected override Component? Render() =>
        Ui.Slider.Value(_amount).TrackClass("h-5").ThumbClass("size-5").OnChange(amount => { _amount = amount; }).Class("max-w-sm");
}
