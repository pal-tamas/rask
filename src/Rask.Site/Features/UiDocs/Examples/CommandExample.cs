namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CommandExample : Component
{
    private string _last = "nothing yet";

    protected override Component? Render() =>
        Div.Class("flex flex-col items-center gap-4")[
            Ui.Command[
                Ui.CommandInput.Placeholder("Search..."),
                Ui.CommandItems[
                    Ui.CommandItem.Icon(Ui.IconName.UserPlus).Kbd("⌘A").OnClick(() => { _last = "assigned"; })["Assign to…"],
                    Ui.CommandItem.Icon(Ui.IconName.DocumentPlus).OnClick(() => { _last = "created a file"; })["Create new file"],
                    Ui.CommandItem.Icon(Ui.IconName.FolderPlus).Kbd("⌘⇧N").OnClick(() => { _last = "created a project"; })["Create new project"],
                    Ui.CommandItem.Icon(Ui.IconName.BookOpen).OnClick(() => { _last = "opened the documentation"; })["Documentation"],
                    Ui.CommandItem.Icon(Ui.IconName.Newspaper).OnClick(() => { _last = "opened the changelog"; })["Changelog"],
                    Ui.CommandItem.Icon(Ui.IconName.Cog6Tooth).Kbd("⌘,").OnClick(() => { _last = "opened the settings"; })["Settings"]
                ]
            ],
            Ui.Text[$"Last command: {_last}."]
        ];
}
