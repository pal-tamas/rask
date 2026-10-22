namespace Rask.Site.Features.UiDocs;

/// <summary>How a slider behaves: the value follows the thumb, a range, the browser's keys, big steps, labels and ticks.</summary>
internal sealed partial class UiSliderBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Value(),
            Keyboard(),
            Field(),
            Ticks()
        ];

    private static UiDocsTopic Value() =>
        UiDocsTopic.Key("value").Heading("The value follows the thumb").Paragraphs([
            "A slider binds a number: an `int`, `long`, `float`, `double` or `decimal`. The bound member (or `OnChange`) "
            + "hears every step while the thumb is dragged, which is what lets a label beside it show the number; a "
            + "slider the page owns draws the `Value` it was given, so keep it in state.",
            "A range is the same slider over an array of two (`int[]`, `double[]`, `decimal[]`), and every change "
            + "writes a new array. The thumbs do not cross, `MinStepsBetween` keeps them that many steps apart, and a "
            + "press on the track moves the nearer one.",
        ]);

    private static Component Keyboard() =>
        UiDocsTopic.Key("keyboard").Heading("The browser does the moving").Paragraphs([
            "Each thumb holds a real `<input type=\"range\">`, invisible and laid over the stretch of track that thumb "
            + "can reach. A range's two inputs say `aria-valuetext=\"200 start range\"` and `\"800 end range\"`, and "
            + "carry their neighbour as their own `max` and `min`. `BigStep` is the one thing a range input cannot do "
            + "alone, so the input carries `data-rask-big-step` and the runtime steps it.",
        ])[
            UiDocsKeys.Column("Key").Rows([
                ("ArrowRight, ArrowUp", "One step up. With Shift, the big step."),
                ("ArrowLeft, ArrowDown", "One step down. With Shift, the big step."),
                ("Page Up, Page Down", "The big step, or a tenth of the track when there is none."),
                ("Home, End", "The two ends, or the other thumb less the steps kept between them."),
            ])
        ];

    private static UiDocsTopic Field() =>
        UiDocsTopic.Key("field").Heading("A label is the field's").Paragraphs([
            "Flux's slider takes no `label` of its own, and neither does this one: put it in a `Ui.Field` with a "
            + "`Ui.Label` (and a `Ui.Description`), and every thumb is named by that label (`aria-labelledby`) and "
            + "described by the rest. A bound slider outside a field still shows its own message.",
        ]);

    private static UiDocsTopic Ticks() =>
        UiDocsTopic.Key("ticks").Heading("Ticks").Paragraphs([
            "`Ui.SliderTick.Value(n)` children mark values: a line, a `.Dot`, or whatever you put inside. They sit under "
            + "the track, or on it with `.Inside` (`TickPosition`). Each carries `data-active` while the fill reaches it "
            + "and `data-current` while a thumb is on it, and pressing one moves the thumb there. A `size-6` in "
            + "`ThumbClass` is also where the fill and the ticks learn the thumb's size (`--ui-slider-thumb`, 1rem "
            + "unless set).",
        ]);
}
