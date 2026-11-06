namespace Rask.Flux.Commands;

/// <summary>
///     Holds Ui.Select's OPEN listbox to Flux UI's live documentation. Parity measures every example as loaded, where a
///     custom select's options are not displayed; this opens each one — on fluxui.dev and on the page FluxParityPages
///     wrote — and compares what is then on screen: the trigger and the whole popup subtree (tag, box, computed styles,
///     pseudo-elements); where the popup sits against its trigger (every box is measured from the trigger's corner); a row
///     hovered, and a row pressed.
/// </summary>
/// <remarks>
///     The measuring, the comparison and the walk are <see cref="OpenList" />'s, shared with every control that opens a
///     list; what is the select's own — its selectors and its walks — is here.
///     <code>
///     dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask page
///     flux parity-select                                            # every custom example, light and dark
///     flux parity-select --refresh                                  # re-measure Flux
///     flux parity-select --section combobox --all                   # one section, every difference
///     </code>
///     Exit code 1 on any difference. The measurements land in artifacts/flux-parity/{flux,rask}/select-open/.
/// </remarks>
[FluxCommand("parity-select", "a select's open listbox against Flux's, and its --live walks")]
public sealed class ParitySelect : IFluxCommand
{
    private const string Listbox = "#ui-select-listbox";
    private const string Searchable = "#ui-select-searchable";
    private const string Combobox = "#ui-select-combobox";

    private static readonly OpenKind Kind = new(
        Slug: "select",
        Name: "select",
        Root: "[data-flux-select], [data-ui-select]",
        Trigger: "button[role=combobox], input[role=combobox]",
        Popup: "[data-flux-options], [data-ui-options]",
        Rows: "[data-flux-option], [data-ui-option]",
        // Flux's custom elements and the native element Rask.Ui writes in their place, with the same role.
        Native: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ui-select"] = "div",
            ["ui-selected"] = "div",
            ["ui-options"] = "div",
            ["ui-option"] = "div",
            ["ui-option-empty"] = "div",
            ["ui-option-create"] = "div",
        },
        Says: "(control, trigger, words) => (trigger.localName === 'input' ? trigger.value : words(trigger))",
        LiveRoot: "el => el.closest('[data-ui-select]').parentElement",
        // Each walk: Flux's example by index, the showcase's control by selector, and the steps.
        Walks:
        [
            new("listbox", 3, Listbox,
            [
                "focus", "ArrowDown", "ArrowDown", "ArrowDown", "ArrowUp", "End", "Home", "PageDown", "PageUp", "Enter",
                "ArrowUp", "ArrowDown", "ArrowDown", "ArrowDown", "ArrowDown", "ArrowDown", "ArrowDown", "Escape", "type:le", "Space", "type:acc", "Space",
                "ArrowDown", "hover:1", "ArrowDown", "click:4", "ArrowDown", "Escape", "ArrowDown", "Tab",
            ]),
            // The pointer and the keys together: a row the pointer lit goes dark when it leaves and one the keys
            // lit stays, the arrows go on from the lit row and start from an end when none is, Enter picks what
            // the pointer lit and nothing when no row is, Space shuts the open list, letters pick on the shut one.
            new("listbox, pointer and keys", 3, Listbox,
            [
                "click", "hover:2", "ArrowDown", "leave", "ArrowDown", "hover:5", "leave", "ArrowUp", "hover:1", "Enter",
                "click", "hover:6", "leave", "Enter", "Escape", "click", "hover:2", "Space", "type:d", "type:o", "click", "ArrowUp", "Escape", "Tab",
            ]),
            // Enter on the CLOSED button. Flux leaves the list shut; a native <button popovertarget> is pressed by
            // Enter, and C# cannot prevent a key's default. The runtime hook that would (contain Enter on a closed
            // listbox button) does not exist — see FluxConformanceTests.NotTranslated. Printed, and not counted.
            new("listbox, Enter on the closed button", 3, Listbox, ["focus", "Enter"],
                "the browser presses a button on Enter; Flux's script does not open on it"),
            new("searchable", 10, Searchable,
            [
                "focus", "ArrowDown", "ArrowDown", "type:co", "ArrowDown", "ArrowUp", "ArrowUp", "type:zz", "Backspace", "Backspace", "Backspace",
                "Enter", "ArrowDown", "type:leg", "Escape", "ArrowDown", "type:á", "Tab",
            ]),
            new("searchable, pointer and keys", 10, Searchable,
            [
                "click", "hover:2", "ArrowDown", "leave", "ArrowDown", "type:co", "hover:5", "leave", "ArrowUp", "Backspace", "Backspace",
                "hover:1", "Enter", "click", "type:w", "Escape", "click", "Tab",
            ]),
            new("multiple", 12, "#ui-select-multiple",
                ["focus", "ArrowDown", "ArrowDown", "Enter", "ArrowDown", "Enter", "Enter", "Escape", "type:o", "ArrowDown", "Tab"]),
            new("combobox", 13, Combobox,
                ["click", "Escape", "type:de", "ArrowDown", "Enter", "ArrowDown", "ArrowDown", "Escape", "Escape", "type:xyz", "Tab"]),
            new("combobox, pointer and keys", 13, Combobox,
            [
                "click", "hover:2", "ArrowDown", "leave", "ArrowDown", "type:de", "hover:2", "leave", "ArrowDown", "Enter",
                "click", "ArrowUp", "Escape", "type:co", "Escape", "Tab",
            ]),
        ])
    {
        // Held at the viewport's edges, and opened there (Placement).
        Edges = [new(0), new(1), new(4), new(7), new(9)],
        AllRows = "[data-flux-option], [data-ui-option], [data-flux-option-create], [data-ui-option-create]",
        Search = "[data-flux-select-search], [data-ui-select-search]",
    };

    /// <inheritdoc />
    public Task<int> Run(string[] args) => OpenList.Run(Kind, args);
}
