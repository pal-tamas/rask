using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary><c>fluxui.dev/components/command</c>, example for example, as the page loads.</summary>
/// <remarks>
///     The palette's open, typed and lit states are seen only once it is used: <c>scripts/flux/parity-command.mjs</c>
///     brings both pages to each of them, the same way, and holds this page to Flux's there.
/// </remarks>
public sealed partial class CommandParity : FluxParity
{
    // The classes the "As a modal" example hands to `class`, as an app's own Tailwind build emits them, each under a
    // name of its own. `shadow-lg` is left out on purpose: Tailwind prints it before the palette's own `shadow-xs`,
    // so in an app, as on Flux's page, it loses to it.
    private const string AppUtilities =
        "<style>@layer utilities{.pc-modal{width:100%;max-width:30rem;margin-block:12vh;max-height:100vh;overflow-y:hidden}"
        + ".pc-palette{border-style:none;display:inline-flex;flex-direction:column;max-height:76vh}"
        // The docs page's own: each example is centred in a row, and the trigger given a width.
        + ".pc-trigger{min-width:180px}}</style>";

    private const string Centred = "display:flex;justify-content:center";

    public override string Page => "command";

    public override IEnumerable<(string Section, Component Example)> Examples() =>
    [
        ("", Div.Style(Centred)[Palette(Ui.Command, Ui.CommandInput.Placeholder("Search..."))]),
        ("as-a-modal", Div.Style(Centred)[Div[
            Raw.Value(AppUtilities),
            Ui.ModalTrigger.Name("search").Shortcut("mod+k")[
                Ui.Input.Of<string>().As(Ui.InputAs.Button).Placeholder("Search...").Icon(Ui.IconName.MagnifyingGlass).Kbd("⌘K").Class("pc-trigger")
            ],
            Ui.Modal.Name("search").Variant(Ui.ModalVariant.Bare).Class("pc-modal")[
                Palette(Ui.Command.Class("pc-palette"), Ui.CommandInput.Placeholder("Search...").Closable())
            ]
        ]]),
    ];

    /// <summary>The six commands both examples list, in a palette.</summary>
    internal static Component Palette(UiCommand command, UiCommandInput input) =>
        command[
            input,
            Ui.CommandItems[
                Ui.CommandItem.Icon(Ui.IconName.UserPlus).Kbd("⌘A")["Assign to…"],
                Ui.CommandItem.Icon(Ui.IconName.DocumentPlus)["Create new file"],
                Ui.CommandItem.Icon(Ui.IconName.FolderPlus).Kbd("⌘⇧N")["Create new project"],
                Ui.CommandItem.Icon(Ui.IconName.BookOpen)["Documentation"],
                Ui.CommandItem.Icon(Ui.IconName.Newspaper)["Changelog"],
                Ui.CommandItem.Icon(Ui.IconName.Cog6Tooth).Kbd("⌘,")["Settings"]
            ]
        ];
}
