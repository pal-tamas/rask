namespace Rask.Site.Features.UiDocs;

/// <summary>
///     How an input behaves: when a bound value is written, what the runtime does in place of Flux's script, groups, and
///     the type the model gives it.
/// </summary>
internal sealed partial class UiInputBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Binding(),
            NoScript(),
            Groups(),
            FromFlux()
        ];

    private static UiDocsTopic Binding() =>
        UiDocsTopic.Key("binding").Heading("When the value is written").Paragraphs([
            "`Bind(() => m.Email)` or `Value(email).OnChange(…)` stands where Flux has `wire:model`. A bound input is "
            + "written with the reader's next action and travels with it, as `wire:model` does; `.Live()` writes as it "
            + "is typed, after a 150 ms pause (`wire:model.live`), `.Debounce(300.Milliseconds)` after a pause of your "
            + "own, and `.Blur()` when the reader leaves the field (`wire:model.blur`). `OnInput` hears every keystroke.",
            "The type comes from the model: `Ui.Input.Bind(() => m.Seats)` over an `int` is a number field with nothing "
            + "said at the call site. A field with nothing in it yet opens on its type alone: "
            + "`Ui.Input.Of<string>().Label(\"Search\")`.",
        ]);

    private static UiDocsTopic NoScript() =>
        UiDocsTopic.Key("runtime").Heading("No script of the kit's").Paragraphs([
            "What Flux does in Alpine is the runtime's, by attribute. The clear button (`data-rask-clear`) empties the "
            + "field in the click and leaves the focus in it, and CSS hides it while the field is empty. The copy button "
            + "(`data-rask-copy`) writes the clipboard in the click and shows a tick for two seconds. `Mask` holds each "
            + "keystroke to its pattern (`data-rask-mask`), and the value drawn and committed is shaped the same way. "
            + "The reveal button is a handler. A file input is a `<label>` around the real input.",
            "`Attributes((\"aria-label\", \"Search keys\"))` forwards an attribute to the `<input>`, as Flux forwards "
            + "what it does not know: the way to name an input that has no label.",
        ]);

    private static UiDocsTopic Groups() =>
        UiDocsTopic.Key("groups").Heading("In a group, label the group").Paragraphs([
            "`Ui.Field[Ui.Label[\"Website\"], Ui.InputGroup[…], Ui.Error]`: the group stands for its input, so the "
            + "field's label and error reach it. A neighbour that is not an input (a button, a native select) joins the "
            + "outline by carrying `data-ui-group-target`, which the kit's button and select already do.",
        ]);

    private static Component FromFlux() =>
        UiDocsTopic.Key("flux").Heading("From Flux").Paragraphs([
            "Flux's attributes, as steps:",
        ])[
            UiDocsKeys.Column("Flux").Rows([
                ("`<flux:input wire:model=\"email\">`", "`Ui.Input.Bind(() => m.Email)`"),
                ("`wire:model.live`, `.live.debounce.300ms`, `wire:model.blur`", "`.Live()`, `.Debounce(300.Milliseconds)`, `.Blur()`"),
                ("`size=\"sm\"`, `variant=\"filled\"`", "`.Sm`, `.Filled`"),
                ("`icon=\"key\"`, `icon:trailing=\"credit-card\"`", "`.Icon(Ui.IconName.Key)`, `.IconTrailing(Ui.IconName.CreditCard)`"),
                ("`<x-slot name=\"iconTrailing\">`", "`.IconTrailing(Ui.Button.Sm.Subtle.Icon(Ui.IconName.XMark))`"),
                ("`input:class=\"font-mono\"`", "`.InputClass(\"font-mono\")`"),
                ("`as=\"button\"`", "`.As(Ui.InputAs.Button)`"),
                ("`mask:dynamic=\"$money($input)\"`", "`.Money()`; `.Money(decimals: \",\", thousands: \" \")` for `$money($input, ',', ' ')`"),
            ])
        ];
}
