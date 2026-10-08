namespace Rask;

/// <summary>Flux's <c>flux:sidebar.search</c>: the search field at the top of a <see cref="UiSidebar" />.</summary>
/// <remarks>
/// As Flux draws it, it is a button that looks like a field — the way into a search dialog or a command
/// palette, which a <c>Ui.ModalTrigger</c> around it opens. Given <see cref="OnInput" /> it is a real field
/// with the same look, for a sidebar that filters its own list.
/// </remarks>
public sealed partial class UiSidebarSearch : Component
{
    private const string Root =
        "relative flex h-10 w-full cursor-default items-center gap-3 rounded-lg bg-zinc-800/5 px-3 py-2 "
        + "text-base leading-[1.375rem] text-zinc-700 sm:text-sm sm:leading-[1.375rem] sidebar-rail:justify-center sidebar-rail:px-0 dark:bg-white/10 dark:text-zinc-200 dark:shadow-none";

    /// <summary>The words shown while nothing has been typed.</summary>
    public string? Placeholder { get; set; }

    /// <summary>What the reader has typed, for a search that is a field.</summary>
    public string? Value { get; set; }

    /// <summary>Runs on every keystroke with the text so far, and makes the search a field.</summary>
    public Callback<string> OnInput { get; set; }

    /// <summary>Classes for the search.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var lens = Div.Class("flex items-center justify-center text-xs text-zinc-400/75")[
            Ui.Icon.Name(Ui.IconName.MagnifyingGlass).Class("size-4")
        ];

        if (OnInput.HasValue)
        {
            return Label.Class(UiClass.Compose(Root, "cursor-text", Class)).Attributes(("data-ui-sidebar-search", ""))[
                lens,
                // Seam: Flux's input. Ui.Input goes here when it lands.
                Input.Value(Value ?? string.Empty)
                    .Type(InputType.Search)
                    .Placeholder(Placeholder ?? "")
                    .Aria("label", Placeholder ?? "Search")
                    .Class(
                        "min-w-0 flex-1 bg-transparent font-medium outline-none placeholder:text-zinc-400 "
                        + "sidebar-rail:hidden dark:placeholder:text-white/40")
                    .Attributes(("data-ui-seam", "input"))
                    .OnInput(OnInput)
            ];
        }

        var button = Button.Type(ButtonType.Button).Class(UiClass.Compose(Root, Class))
            .Attributes(("data-ui-sidebar-search", ""));
        // Seam: Flux wraps the search in its tooltip, shown beside the rail. Ui.Tooltip goes here when it lands.
        return Div.Class("flex").Attributes(("data-ui-seam", "tooltip"))[
            button[
                lens,
                Div.Class("flex-1 text-start font-medium text-zinc-400 sidebar-rail:sr-only dark:text-white/40")[Placeholder ?? ""]
            ]
        ];
    }
}
