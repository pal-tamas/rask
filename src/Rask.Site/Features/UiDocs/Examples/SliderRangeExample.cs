namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SliderRangeExample : Component
{
    private int[] Range { get; set; } = [20, 80];

    protected override Component? Render() =>
        Ui.Slider.Bind(() => Range).Range().Class("max-w-sm");
}
