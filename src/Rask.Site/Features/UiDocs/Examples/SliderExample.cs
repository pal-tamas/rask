namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SliderExample : Component
{
    private int Amount { get; set; } = 50;

    protected override Component? Render() =>
        Ui.Slider.Bind(() => Amount).Class("max-w-sm");
}
