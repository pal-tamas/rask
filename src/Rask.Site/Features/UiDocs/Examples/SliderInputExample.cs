namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SliderInputExample : Component
{
    private int Amount { get; set; } = 25;

    protected override Component? Render() =>
        Ui.Field.Class("max-w-sm")[
            Ui.Label["Corner radius"],
            Div.Class("-mt-2 flex items-center gap-4")[
                Ui.Slider.Bind(() => Amount).Live(),
                Ui.Input.Bind(() => Amount).Live().Type(InputType.Number).Sm.Class("max-w-18")
            ]
        ];
}
