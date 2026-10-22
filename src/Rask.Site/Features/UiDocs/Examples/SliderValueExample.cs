using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SliderValueExample : Component
{
    private int Amount { get; set; } = 50;

    protected override Component? Render() =>
        Ui.Field.Class("max-w-sm")[
            Ui.Label.Trailing(Span.Class("tabular-nums").Data("testid", "ui-slider-amount")[Amount.ToString(CultureInfo.InvariantCulture)])[
                "Corner radius"
            ],
            Ui.Slider.Bind(() => Amount).Live()
        ];
}
