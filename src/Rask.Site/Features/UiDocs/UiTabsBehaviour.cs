namespace Rask.Site.Features.UiDocs;

/// <summary>
///     How tabs behave: the selected tab as a value, the browser switching panels, forms in tabs, the keyboard, why a
///     tab is not a link, and Flux's attributes as steps.
/// </summary>
internal sealed partial class UiTabsBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Value(),
            Panels(),
            Browser(),
            Keyboard(),
            NotALink(),
            FromFlux()
        ];

    private static UiDocsTopic Value() =>
        UiDocsTopic.Key("value").Heading("The selected tab is the row's value").Paragraphs([
            "The row's value is the selected tab's `Name`, where Flux has `wire:model`. `Bind(() => Tab)` two-way "
            + "binds it to a property, `Value(_tab).OnChange(t => _tab = t)` leaves it with the page, and with neither "
            + "the row keeps track itself, starting on the tab that says `Selected()` or else the first one that is "
            + "not disabled. A tab with no `Name` is known by its place in the row: `\"0\"`, `\"1\"`…",
            "`Bind` writes the property and redraws the row and its panels; like every bound control it does not "
            + "redraw the page around them. A page that shows the selected name somewhere else takes it from "
            + "`OnChange`, which runs in every mode.",
        ]);

    private static UiDocsTopic Panels() =>
        UiDocsTopic.Key("panels").Heading("Panels").Paragraphs([
            "A row needs no group: a segmented List, Board, Timeline on its own is a choice the page reads from "
            + "`OnChange`. Inside a `Ui.TabGroup`, write the row before its panels: a panel learns which tab is "
            + "selected from the row above it. The group ties each tab to the panel of the same name, "
            + "`aria-controls` one way and `aria-labelledby` the other, so a screen reader announces a panel with "
            + "the words on its tab.",
            "Every panel is rendered and the ones not shown are `hidden`; in a `Findable()` group they are "
            + "`hidden=\"until-found\"` instead, so the browser's find-in-page reaches them and a match selects its "
            + "tab. A panel has Flux's top padding (`pt-8`); another is said as Flux's own examples say it, "
            + "`Class(\"pt-6!\")`. A count beside a label is a child, like any other content: "
            + "`Ui.Tab.Name(\"open\")[\"Open\", Ui.Badge[\"12\"]]`.",
        ]);

    private static UiDocsTopic Browser() =>
        UiDocsTopic.Key("browser").Heading("Switching is the browser's").Paragraphs([
            "Selecting a tab is the browser's, as it is Flux's script's: a press moves `aria-selected`, the tab stop "
            + "and `data-selected`, shows the tab's panel, and by itself sends the server nothing. What the page reads "
            + "is the row's value, by its binding's timing: `.Bind(() => Tab)` writes it with the reader's next "
            + "action, `.Bind(() => Tab).Live()` and `.OnChange(…)` hear it at once, and so does a `Ui.Tab`'s own "
            + "`OnClick`.",
            "The page keeps the last word: set the bound property (or pass another `Value`) from a handler and that "
            + "tab is shown, whatever the reader last pressed. That is what makes a long form in tabs work: the fields "
            + "on the panels that are not shown stay in the form with what was typed into them and are submitted with "
            + "it, and a save that is refused sets the tab to the one with the first mistake.",
            "A tab switch never counts as an unsaved change in a `Form.Model(m).ConfirmLeave(\"…\")` form, bound or "
            + "not: the field the row writes the chosen tab into says `data-rask-unguarded`, because a tab is never "
            + "data the reader would lose.",
        ]);

    private static Component Keyboard() =>
        UiDocsTopic.Key("keyboard").Heading("The keyboard").Paragraphs([
            "The keys are the runtime's, for every `role=\"tablist\"`. The arrows select as they go, as Flux's do, "
            + "and only the selected tab is a tab stop. Home and End are not handled, as Flux does not handle them.",
        ])[
            UiDocsKeys.Column("Key").Rows([
                ("ArrowRight, ArrowDown", "Selects the next tab that is not disabled, wrapping from the last to the first."),
                ("ArrowLeft, ArrowUp", "Selects the previous one, wrapping from the first to the last."),
                ("Tab", "Leaves the row for the panel the selected tab shows, which is a tab stop."),
                ("An `Action()` tab", "Is an ordinary button in the row: a tab stop of its own, which the arrows pass over."),
            ])
        ];

    private static UiDocsTopic NotALink() =>
        UiDocsTopic.Key("link").Heading("A tab is not a link").Paragraphs([
            "Flux's tab takes no `href`, and neither does this one: for full-page navigation Flux says to use the "
            + "navbar. A row whose choice is part of the address reads it from `OnChange` and goes there: "
            + "`Ui.Tabs.Value(view).OnChange(v => Go.To(Routes.LogsPage(View: v)))`.",
        ]);

    private static Component FromFlux() =>
        UiDocsTopic.Key("flux").Heading("From Flux").Paragraphs([
            "Flux's attributes, as steps:",
        ])[
            UiDocsKeys.Column("Flux").Rows([
                ("`<flux:tabs variant=\"segmented\" size=\"sm\">`", "`Ui.Tabs.Segmented.Size(Ui.TabsSize.Sm)`"),
                ("`<flux:tabs variant=\"pills\">`", "`Ui.Tabs.Pills`"),
                ("`<flux:tabs scrollable scrollable:fade scrollable:scrollbar=\"hide\">`",
                    "`Ui.Tabs.Scrollable().ScrollableFade().ScrollableScrollbar(Ui.TabsScrollbar.Hide)`"),
                ("`<flux:tabs class=\"px-4\">`", "`Ui.Tabs.Class(\"px-4\")`"),
                ("`<flux:tab icon=\"user\" icon:trailing=\"chevron-down\" icon:variant=\"solid\">`",
                    "`Ui.Tab.Icon(Ui.IconName.User).IconTrailing(Ui.IconName.ChevronDown).IconVariant(Ui.IconVariant.Solid)`"),
                ("`<flux:tab selected>`, `disabled`, `:accent=\"false\"`", "`.Selected()`, `.Disabled()`, `.Accent(false)`"),
                ("`<flux:tab icon=\"plus\" wire:click=\"addTab\" action>`", "`Ui.Tab.Icon(Ui.IconName.Plus).Action().OnClick(AddTab)`"),
                ("`<flux:tab.group findable>`", "`Ui.TabGroup.Findable()`"),
            ])
        ];
}
