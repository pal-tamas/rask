namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SliderDotsExample : Component
{
    private int _step = 4;

    protected override Component? Render() =>
        Ui.Slider.Value(_step).Inside.Min(1).Max(5).TrackClass("h-5").ThumbClass("size-6").OnChange(step => { _step = step; })
            .Class("max-w-sm")[
            Enumerable.Range(1, 5).Select(step => Ui.SliderTick.Key(step).Value(step).Dot)
        ];
}
