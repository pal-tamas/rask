namespace Rask.Site.Features.UiDocs;

/// <summary>How a toggle behaves: a real switch, whose state it is, and the one that sends nothing.</summary>
internal sealed partial class UiToggleBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Switch(),
            State(),
            Browser()
        ];

    private static UiDocsTopic Switch() =>
        UiDocsTopic.Key("switch").Heading("A real switch").Paragraphs([
            "It is a `<button role=\"switch\">`: a press, Space and Enter turn it, and a screen reader says \"on\" or "
            + "\"off\". A toggle that shows only an icon is named by its `Tooltip`. `Icon` draws a Heroicon's outline "
            + "when off and its solid drawing when on, and `OnIcon`, `OffIcon`, `OnLabel` and `OffLabel` say each state "
            + "apart.",
        ]);

    private static UiDocsTopic State() =>
        UiDocsTopic.Key("state").Heading("Whose state it is").Paragraphs([
            "`Bind` two-way binds a `bool`: the member is written with the next action, as every kit control's is, and "
            + "at once with `.Live()`. A bound toggle the page listens to with `OnChange` is written at once too, and "
            + "`OnChange` runs after the write: Flux's `wire:model` beside `wire:change`, one round trip that saves. While "
            + "its handler runs the runtime marks the button `data-loading`: what it shows gives way to a spinner and it "
            + "takes no further press.",
            "Unbound and listened to with `OnChange`, it reports each turn, and a page that hands it a different "
            + "`Checked` has the last word.",
        ]);

    private static UiDocsTopic Browser() =>
        UiDocsTopic.Key("browser").Heading("A toggle nobody listens to sends nothing").Paragraphs([
            "With no `Bind` and no `OnChange` a press only changes what the toggle draws, so the browser does it "
            + "(`data-rask-switch`): `aria-checked` and `data-checked` flip there, and not one frame goes to the page. "
            + "`Checked` is then where it starts.",
        ]);
}
