using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's slider and its ticks as C# components: each example with its own code, and its reference.</summary>
[Route("slider")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiSliderPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Slider.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Slider)
            .Lead("A value chosen along a track, or a range between two thumbs. Each thumb is a real range input, so "
                  + "dragging, a press on the track and the keys are the browser's own.")
            .Api(UiApi.Slider)[
            UiDocsExample.Key("ui-slider").Heading("Slider").File("SliderExample.cs").Demo(SliderExample).Testid("ui-slider"),
            UiDocsExample.Key("ui-slider-step").Heading("Min/max/step").File("SliderStepExample.cs").Demo(SliderStepExample).Testid("ui-slider-step"),
            UiDocsExample.Key("ui-slider-value").Heading("Displaying value").File("SliderValueExample.cs").Demo(SliderValueExample).Testid("ui-slider-value")
                .Blurb("The value follows the thumb while it moves, so a label beside it can show the number."),
            UiDocsExample.Key("ui-slider-input").Heading("With input").File("SliderInputExample.cs").Demo(SliderInputExample).Testid("ui-slider-input")
                .Blurb("A number input beside the slider, both bound to the same member, in one field."),
            UiDocsExample.Key("ui-slider-big").Heading("Big steps").File("SliderBigStepExample.cs").Demo(SliderBigStepExample).Testid("ui-slider-big")
                .Blurb("Hold Shift and press an arrow key to move by the big step; Page Up and Page Down move by it too."),
            UiDocsExample.Key("ui-slider-marks").Heading("Step marks").File("SliderMarksExample.cs").Demo(SliderMarksExample).Testid("ui-slider-marks"),
            UiDocsExample.Key("ui-slider-dots").Heading("Step dots").File("SliderDotsExample.cs").Demo(SliderDotsExample).Testid("ui-slider-dots")
                .Blurb("Inside puts the ticks on the track, and Dot draws each as a dot."),
            UiDocsExample.Key("ui-slider-numbers").Heading("Numbered steps").File("SliderNumbersExample.cs").Demo(SliderNumbersExample).Testid("ui-slider-numbers"),
            UiDocsExample.Key("ui-slider-ticks").Heading("Custom steps").File("SliderCustomStepsExample.cs").Demo(SliderCustomStepsExample).Testid("ui-slider-ticks")
                .Blurb("A tick's children are its label. Pressing one moves the thumb there."),
            UiDocsExample.Key("ui-slider-range").Heading("Range slider").File("SliderRangeExample.cs").Demo(SliderRangeExample).Testid("ui-slider-range")
                .Blurb("Two thumbs over an array of two: int[] Range = [20, 80]."),
            UiDocsExample.Key("ui-slider-apart").Heading("Min steps between").File("SliderMinStepsExample.cs").Demo(SliderMinStepsExample).Testid("ui-slider-apart")
                .Blurb("The thumbs never come closer than ten steps."),
            UiDocsExample.Key("ui-slider-price").Heading("Displaying values").File("SliderRangeValuesExample.cs").Demo(SliderRangeValuesExample).Testid("ui-slider-prices"),
            UiDocsExample.Key("ui-slider-styles").Heading("Custom styles").File("SliderStylesExample.cs").Demo(SliderStylesExample).Testid("ui-slider-styles")
                .Blurb("TrackClass and ThumbClass style the two parts."),
            UiSliderBehaviour
        ];
}
