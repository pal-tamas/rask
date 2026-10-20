namespace Rask.Site.Features.UiDocs;

/// <summary>How a command palette behaves: what runs in the browser, its box, and the palette as a modal.</summary>
internal sealed partial class UiCommandBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            UiDocsTopic.Key("browser").Heading("In the browser").Paragraphs([
                "`OnClick` is Flux's `wire:click`: what pressing the command does. Everything else is the browser's, as "
                + "Flux's custom element keeps it (`data-rask-command`), and none of it sends the server anything.",
            ])[
                UiDocsKeys.Column("When").Rows([
                    ("Focus enters the box", "The first command is lit."),
                    ("Typing", "Hides the commands that do not contain it, in any case and with any accents, lights "
                               + "the first one left, and shows \"No results found\" when none is."),
                    ("`ArrowUp` / `ArrowDown`", "Move the lit command, and stop at the ends."),
                    ("The pointer", "Lights the command under it."),
                    ("`Enter`", "Presses the lit command."),
                    ("A command is pressed", "The box empties and nothing else: the palette stays where it is."),
                ])
            ],
            UiDocsTopic.Key("parts").Heading("The box and the rows").Paragraphs([
                "`Ui.CommandInput` takes Flux's `Icon` (the magnifying glass unless set), `Placeholder`, `Clearable` and "
                + "`Closable`. Flux draws the clear button whether or not it is asked to (measured), so it is there "
                + "unless `Clearable(false)`, or the box is `Closable` and not also clearable.",
                "`Ui.CommandItem` takes `Icon`, `IconVariant` (outline unless set) and `Kbd`, a hint: the page binds the "
                + "key.",
            ]),
            UiDocsTopic.Key("modal").Heading("As a modal").Paragraphs([
                "Flux's \"As a modal\" example is a bare `Ui.Modal` opened by a trigger, and from anywhere on the page "
                + "by its `Shortcut`; `Closable` puts the box's close button on it.",
                "`data-rask-shortcut=\"mod+k\"` clicks its element when the combination is pressed: `mod` is ⌘ on a Mac "
                + "and Ctrl elsewhere, beside `ctrl`, `alt`, `shift` and `meta`. A shortcut with no modifier does not "
                + "fire while the reader is typing.",
                "As on Flux's page, the modal opens with focus on nothing inside it (Tab takes it into the box), Escape "
                + "closes it, and what was typed is still there when it opens again. Flux caps the palette at 480px only "
                + "while it is handed no class; the modal example's classes take the cap away.",
            ])
        ];
}
