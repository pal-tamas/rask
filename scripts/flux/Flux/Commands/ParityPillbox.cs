namespace Rask.Flux.Commands;

/// <summary>
///     Holds Ui.Pillbox's OPEN list to Flux UI's live documentation: parity measures the page as loaded, where the list is
///     not displayed. The measuring, the comparison and the walk are <see cref="OpenList" />'s; what is the pillbox's own —
///     its selectors and its walks — is here.
/// </summary>
/// <remarks>
///     <code>
///     dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask page
///     flux parity-pillbox [--refresh] [--all]
///     flux parity-pillbox --record                                  # Flux's walks alone, every step
///     flux parity-pillbox --live URL-OF-THE-DEMO                    # the walks, against a running site
///     </code>
///     Exit code 1 on any difference. The measurements land in artifacts/flux-parity/{flux,rask}/pillbox-open/.
/// </remarks>
[FluxCommand("parity-pillbox", "a pillbox's open list against Flux's, and its --live walks")]
public sealed class ParityPillbox : IFluxCommand
{
    private const string Tags = "#ui-pillbox-tags";

    private static readonly OpenKind Kind = new(
        Slug: "pillbox",
        Name: "pillbox",
        Root: "[data-flux-pillbox], [data-ui-pillbox]",
        Trigger: "[data-flux-pillbox-trigger], [data-ui-pillbox-trigger]",
        Popup: "[popover]",
        Rows: "[data-flux-listbox-option], [data-ui-listbox-option]",
        // Flux's custom elements and the native element Rask.Ui writes in their place, with the same role.
        Native: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ui-pillbox"] = "div",
            ["ui-pillbox-trigger"] = "div",
            ["ui-selected"] = "div",
            ["ui-options"] = "div",
            ["ui-option"] = "div",
            ["ui-option-empty"] = "div",
            ["ui-option-create"] = "div",
            ["ui-selected-remove"] = "div",
        },
        // The pills, then what the inline input holds.
        Says: "(control, trigger, words) => words(trigger) + (trigger.querySelector('input') ? ` [${trigger.querySelector('input').value}]` : '')",
        LiveRoot: "el => el.closest('[data-ui-pillbox]').parentElement",
        Walks:
        [
            new("default", 0, Tags,
            [
                "focus", "ArrowDown", "ArrowDown", "ArrowDown", "ArrowUp", "End", "Home", "Enter", "ArrowDown", "Enter", "Enter", "Escape",
                "Backspace", "type:s", "ArrowDown", "ArrowDown", "hover:1", "ArrowDown", "click:4", "Escape", "Enter", "Tab",
            ]),
            // Space opens the closed list on both. On Rask the page behind it scrolls as well: the trigger is no
            // button, and nothing can prevent a key's default from C#. See FluxConformanceTests.NotTranslated.
            new("Space on the closed trigger", 0, Tags, ["focus", "Space", "Escape"]),
            new("pointer", 0, Tags,
            [
                "click", "click:1", "leave", "ArrowDown", "hover:3", "leave", "ArrowDown", "Escape", "remove:0", "click", "click:2", "click:3", "Escape",
                "remove:0",
            ]),
            new("pointer and keys", 0, Tags,
            [
                "click", "ArrowDown", "ArrowDown", "leave", "ArrowDown", "hover:4", "leave", "ArrowDown", "hover:1", "ArrowDown", "Enter",
                "ArrowDown", "click:4", "leave", "ArrowDown", "Escape", "type:p", "type:p", "click", "End", "Home", "ArrowUp", "Tab",
            ]),
            new("searchable", 2, "#ui-pillbox-searchable",
            [
                "click", "type:p", "ArrowDown", "Enter", "type:zz", "Backspace", "Backspace", "ArrowDown", "Enter", "Escape", "ArrowDown", "Tab",
            ]),
            new("combobox", 4, "#ui-pillbox-combobox",
            [
                "click", "ArrowDown", "Enter", "type:ru", "Enter", "type:zz", "Backspace", "Backspace", "Backspace", "Backspace", "Escape", "Escape",
                "ArrowDown", "Tab",
            ]),
        ])
    {
        RaskPage = "pillbox-picked",
        Pick = [1, 2],
        // A click on a row takes focus to the list; the input among the pills keeps it.
        Focused = ["[data-ui-pillbox-trigger] [data-ui-pillbox-input]", "[role=listbox]"],
        // Held at the viewport's edges, and opened there (Placement).
        Edges = [new(0), new(1), new(2)],
        AllRows = "[data-flux-listbox-option], [data-ui-listbox-option], [data-flux-option-create], [data-ui-option-create]",
        Search = "[data-flux-pillbox-search], [data-ui-pillbox-search]",
        Remove = "ui-selected-remove, [data-ui-pillbox-trigger] [data-value] > :last-child",
    };

    /// <inheritdoc />
    public Task<int> Run(string[] args) => OpenList.Run(Kind, args);
}
