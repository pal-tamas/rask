using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SliderBigStepExample : Component
{
    private int _amount = 50;

    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-3")[
            Ui.Slider.Value(_amount).Step(1).BigStep(10).OnChange(amount => { _amount = amount; }),
            Ui.Text[
                "Amount: ",
                Span.Class("tabular-nums").Data("testid", "ui-slider-big-value")[_amount.ToString(CultureInfo.InvariantCulture)]
            ]
        ];
}
