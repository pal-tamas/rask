using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SliderRangeValuesExample : Component
{
    private int[] Range { get; set; } = [200, 800];

    protected override Component? Render() =>
        Div.Class("relative max-w-sm")[
            Ui.Field[
                Ui.Label.Trailing(Span.Data("testid", "ui-slider-price")[
                    "$", Span.Class("tabular-nums")[Number(Range[0])], " – $", Span.Class("tabular-nums")[Number(Range[1])]
                ])["Price range"],
                Ui.Slider.Bind(() => Range).Live().Range().Min(0).Max(990).Step(10).MinStepsBetween(10).BigStep(100)
            ]
        ];

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
