namespace Rask.Site.Features.UiDocs;

/// <summary>How a switch behaves: a real checkbox, the keys, and its value.</summary>
internal sealed partial class UiSwitchBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            RealInput(),
            Value()
        ];

    private static UiDocsTopic RealInput() =>
        UiDocsTopic.Key("input").Heading("A real checkbox").Paragraphs([
            "The switch is a `<label>` around an `<input type=\"checkbox\" role=\"switch\">` that is out of sight, never "
            + "`hidden`. The thumb's 150 ms travel reads the input's own `:checked`, so a click on the label, the space "
            + "bar and the form post (`name` and `value`) are the browser's. A native checkbox does not flip on Enter; "
            + "Flux's switch does, so the runtime flips every `role=\"switch\"` on Enter as well.",
        ]);

    private static UiDocsTopic Value() =>
        UiDocsTopic.Key("value").Heading("Its value").Paragraphs([
            "A switch binds a `bool`: `Bind(() => m.Alerts)` writes with the reader's next action and `.Live()` as it "
            + "is flipped, which is how a setting that saves itself is written; `Value(on).OnChange(v => on = v)` leaves "
            + "it with the page. It sits at the far edge of its row, and `Left` puts it before its label. `Name` is "
            + "what it is posted under, and `Attributes` forwards anything else to the input.",
        ]);
}
