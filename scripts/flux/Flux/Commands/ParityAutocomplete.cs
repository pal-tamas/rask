namespace Rask.Flux.Commands;

/// <summary>
///     Holds Ui.Autocomplete's OPEN list to Flux UI's live documentation: parity measures the page as loaded, where the list
///     is not displayed. The measuring, the comparison and the walk are <see cref="OpenList" />'s; what is the
///     autocomplete's own — its selectors and its walks — is here.
/// </summary>
/// <remarks>
///     <code>
///     dotnet test tests/Rask.Ui.Tests --filter FluxParityPages          # writes the Rask page
///     flux parity-autocomplete [--refresh] [--all]
///     flux parity-autocomplete --record                                  # Flux's walks alone, every step
///     flux parity-autocomplete --live URL-OF-THE-DEMO                    # the walks, against a running site
///     </code>
///     Exit code 1 on any difference. The measurements land in artifacts/flux-parity/{flux,rask}/autocomplete-open/.
/// </remarks>
[FluxCommand("parity-autocomplete", "an autocomplete's open list against Flux's, and its --live walks")]
public sealed class ParityAutocomplete : IFluxCommand
{
    private const string State = "#ui-autocomplete-state";

    private static readonly OpenKind Kind = new(
        Slug: "autocomplete",
        Name: "autocomplete",
        Root: "[data-flux-autocomplete], [data-ui-autocomplete]",
        Trigger: "input[role=combobox]",
        Popup: "[data-flux-autocomplete-items], [data-ui-autocomplete-items]",
        Rows: "[data-flux-autocomplete-item], [data-ui-autocomplete-item]",
        // Flux's custom elements and the native element Rask.Ui writes in their place, with the same role.
        Native: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ui-select"] = "div",
            ["ui-field"] = "div",
            ["ui-label"] = "label",
            ["ui-options"] = "div",
            ["ui-option"] = "div",
            ["ui-empty"] = "div",
        },
        Says: "(control, trigger) => trigger.value",
        LiveRoot: "el => el.closest('[data-ui-autocomplete]').parentElement",
        Walks:
        [
            new("keys", 0, State,
            [
                "focus", "ArrowDown", "ArrowDown", "ArrowUp", "ArrowUp", "End", "Home", "Enter", "ArrowDown", "Escape", "Escape",
                "type:x", "Backspace", "Backspace", "Backspace", "Backspace", "Backspace", "Backspace", "Backspace", "Backspace",
                "type:ne", "ArrowDown", "ArrowDown", "Enter", "type:zz", "Enter", "Tab",
            ]),
            new("pointer", 0, State, ["click", "hover:2", "ArrowDown", "click:4", "click", "type:x", "Escape", "click", "Tab"]),
            new("pointer and keys", 0, State,
            [
                "click", "hover:3", "leave", "ArrowDown", "ArrowDown", "leave", "ArrowUp", "hover:1", "Enter", "ArrowDown", "ArrowUp",
                "Escape", "Tab",
            ]),
        ])
    {
        // Held at the viewport's edges, and opened there (Placement).
        Edges = [new(0), new(1)],
    };

    /// <inheritdoc />
    public Task<int> Run(string[] args) => OpenList.Run(Kind, args);
}
