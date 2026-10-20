namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CommandModalExample : Component
{
    private string _last = "nothing yet";

    protected override Component? Render() =>
        Div.Class("flex flex-col items-center gap-4")[
            // ⌘K, or Ctrl K off a Mac, presses the trigger from anywhere on the page.
            Ui.ModalTrigger.Name("search").Shortcut("mod+k")[
                Ui.Input.Of<string>().As(Ui.InputAs.Button).Placeholder("Search...").Icon(Ui.IconName.MagnifyingGlass).Kbd("⌘K")
            ],
            Ui.Modal.Name("search").Variant(Ui.ModalVariant.Bare)
                .Class("my-[12vh] max-h-screen w-full max-w-[30rem] overflow-y-hidden")[
                Ui.Command.Class("inline-flex max-h-[76vh] flex-col border-none shadow-lg")[
                    Ui.CommandInput.Placeholder("Search...").Closable(),
                    Ui.CommandItems[
                        Ui.CommandItem.Icon(Ui.IconName.UserPlus).Kbd("⌘A").OnClick(() => { _last = "assigned"; })["Assign to…"],
                        Ui.CommandItem.Icon(Ui.IconName.DocumentPlus).OnClick(() => { _last = "created a file"; })["Create new file"],
                        Ui.CommandItem.Icon(Ui.IconName.FolderPlus).Kbd("⌘⇧N").OnClick(() => { _last = "created a project"; })["Create new project"],
                        Ui.CommandItem.Icon(Ui.IconName.BookOpen).OnClick(() => { _last = "opened the documentation"; })["Documentation"],
                        Ui.CommandItem.Icon(Ui.IconName.Newspaper).OnClick(() => { _last = "opened the changelog"; })["Changelog"],
                        Ui.CommandItem.Icon(Ui.IconName.Cog6Tooth).Kbd("⌘,").OnClick(() => { _last = "opened the settings"; })["Settings"]
                    ]
                ]
            ],
            Ui.Text[$"Last command: {_last}."]
        ];
}
