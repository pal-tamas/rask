namespace Rask.Site.Features.UiDocs;

/// <summary>How an OTP input behaves: Flux's keys, one hidden field for the code, completion, and its markup.</summary>
internal sealed partial class UiOtpBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Keyboard(),
            OneField(),
            Semantics()
        ];

    private static Component Keyboard() =>
        UiDocsTopic.Key("keyboard").Heading("The keys are Flux's").Paragraphs([
            "The cells are the browser's while they are typed into: the group carries `data-rask-otp` and the runtime "
            + "moves between them inside the key's own event, so typing as fast as a keyboard allows loses nothing and "
            + "no key waits for a round trip.",
        ])[
            UiDocsKeys.Column("Key").Rows([
                ("A character", "Fills the cell and moves on; over a filled cell it replaces the character. A character the `Mode` does not take is refused."),
                ("Backspace", "Deletes and steps back; in an empty cell it only steps back."),
                ("Delete", "Closes the code up from where it is."),
                ("ArrowLeft, ArrowRight", "Walk the cells, and stop at the first empty one."),
                ("A paste, or the code the phone offers", "Fills the cells from the first, keeping only what the `Mode` takes."),
            ])
        ];

    private static UiDocsTopic OneField() =>
        UiDocsTopic.Key("code").Heading("One field for the code").Paragraphs([
            "The cells are rendered with no `value` and no handler, and one `<input type=\"hidden\">` inside the group "
            + "carries the code. That field is what `Bind`, or `Value` with `OnChange`, sees, and `Name` is its `name`, "
            + "so a plain form posts the code once. Set the bound string yourself (to clear a wrong code) and the cells "
            + "follow. The code has no gaps and holds only what the `Mode` takes: digits, or `.Alphanumeric` and "
            + "`.Alpha` with letters upper-cased in the value, as Flux's are.",
            "`OnComplete` runs with the code each time its last cell is filled, bound or not. It stands where Flux's "
            + "`submit=\"auto\"` submits the form; the form itself is not submitted for you.",
        ]);

    private static UiDocsTopic Semantics() =>
        UiDocsTopic.Key("markup").Heading("Its markup").Paragraphs([
            "Flux's: a `role=\"group\"` named by the field's label, each cell named \"Character 2 of 6\", one tab stop "
            + "(the first empty cell), `autocomplete=\"one-time-code\"` on the first cell (`Autocomplete(\"off\")` stops "
            + "the browser offering a code), `inputmode=\"numeric\"` for digits and `type=\"password\"` with `Private()`. "
            + "An invalid code is the bound member's message, which marks every cell, and `Disabled()` disables every "
            + "cell. Flux's `label:sr-only`, `:error:icon=\"false\"` and `error:class` are `LabelSrOnly()`, "
            + "`ErrorIcon(false)` and `ErrorClass(\"text-center\")`.",
        ]);
}
