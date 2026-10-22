namespace Rask.Site.Features.UiDocs;

/// <summary>How a colour picker behaves: its value, the keys, the sheet on a phone, typed colours and the runtime.</summary>
internal sealed partial class UiColorPickerBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Value(),
            Keyboard(),
            Phone(),
            Typed(),
            NoScript()
        ];

    private static UiDocsTopic Value() =>
        UiDocsTopic.Key("value").Heading("Its value").Paragraphs([
            "The value is CSS text in `Format`, whatever CSS it was given in, and no colour is the empty string. A bound "
            + "colour is written with the reader's next action, `.Live()` writes it as it is chosen, and under a pointer "
            + "every change is the page's at once. `Name` adds a hidden input for a plain form post.",
        ]);

    private static Component Keyboard() =>
        UiDocsTopic.Key("keyboard").Heading("The keyboard").Paragraphs([
            "The swatch button, the button trigger or ArrowDown in the input opens the popover and focuses the area; "
            + "Escape and a press outside close it and the focus returns.",
        ])[
            UiDocsKeys.Column("Key").Rows([
                ("The arrows, on the area", "Move the thumb by 2, and by 10 with Shift."),
                ("PageUp, PageDown, Home, End, on the area", "Full and no brightness; no and full saturation."),
                ("The arrows, on a slider", "Step by 1, by 10 with Shift, and by a tenth of the track with a page key."),
                ("The arrows, on the palette", "Walk it eight to a row; Enter or Space picks. The palette is one tab stop."),
                ("Enter, or leaving the input", "Takes a typed colour."),
            ])
        ];

    private static UiDocsTopic Phone() =>
        UiDocsTopic.Key("phone").Heading("On a phone it is a sheet").Paragraphs([
            "Below `sm` the popover is centred over the page, which does not scroll behind it, and it keeps what is "
            + "chosen until Select: Cancel, Escape and a press outside leave the colour as it was. A swatch does not "
            + "wait: it is taken, and the sheet closes.",
        ]);

    private static UiDocsTopic Typed() =>
        UiDocsTopic.Key("typed").Heading("What a typed colour becomes").Paragraphs([
            "Hex, a CSS colour name, `rgb()`, `hsl()` and `hwb()` are read as CSS reads them. Flux hands every other "
            + "form to the browser and reads the numbers of the colour the browser writes back as red, green and blue, "
            + "so the wide-gamut functions come out as something else: `oklch(0.7 0.1 200)` is `#0100c8`. The kit "
            + "reproduces it digit for digit, by the owner's decision.",
            "Text that is no colour is refused, not put back, which is the one departure from Flux, and only in a form: "
            + "a bound picker keeps `#12` in the field, says \"Enter a valid color.\" under it with the next action and "
            + "holds the save. Outside a form it puts the last colour back, as Flux does.",
        ]);

    private static UiDocsTopic NoScript() =>
        UiDocsTopic.Key("runtime").Heading("No script of the kit's").Paragraphs([
            "The popover is a native modal `<dialog>` the button opens by `command=\"show-modal\"`, hung under the "
            + "trigger by CSS anchor rules. The area and each track are `data-rask-drag` surfaces, so the thumb is under "
            + "the pointer every frame; the sliders hold native range inputs for the keyboard; a press on the area leaves "
            + "the focus where it was (`data-rask-press-keeps-focus`); the dropper shows where the browser has one "
            + "(`data-rask-requires`); and the copy button writes the clipboard in the click (`data-rask-copy`). The "
            + "colour maths and every key are C#.",
        ]);
}
