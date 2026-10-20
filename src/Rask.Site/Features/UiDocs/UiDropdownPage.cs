using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's dropdown and menu as C# components: each example with its own code, and their reference.</summary>
[Route("dropdown")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiDropdownPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Dropdown.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Dropdown)
            .Lead("A button and the menu it opens. The first child of Ui.Dropdown is the trigger, any button; the "
                  + "second is what it opens: a Ui.Menu of actions, a Ui.Navmenu of links, or a Ui.Popover. The "
                  + "browser opens and closes it, and the page hears only a pick.")
            .Api(UiApi.Dropdown)[
            UiDocsExample.Key("ui-dropdown-intro").Heading("Dropdown").File("DropdownExample.cs").Demo(DropdownExample).Testid("ui-dropdown-intro")
                .Blurb("A menu with a submenu of radios, one of checkboxes and a danger row. It opens with focus on "
                       + "the menu: the arrows walk the rows and stop at the ends, a letter jumps, Right or Enter "
                       + "opens a submenu and Left closes it, Enter or Space picks, Escape and Tab leave."),
            UiDocsExample.Key("ui-dropdown-navigation").Heading("Navigation menus").File("DropdownNavigationExample.cs").Demo(DropdownNavigationExample).Testid("ui-dropdown-navigation")
                .Blurb("A Ui.Navmenu is links, not a menu: no menu roles and no cursor, so focus stays on the "
                       + "button and Tab walks the links."),
            UiDocsExample.Key("ui-dropdown-position").Heading("Positioning").File("DropdownPositionExample.cs").Demo(DropdownPositionExample).Testid("ui-dropdown-position")
                .Blurb("A side and an alignment, each a step. With no room there it takes the other side, and it "
                       + "always stays inside the viewport."),
            UiDocsExample.Key("ui-dropdown-offset").Heading("Offset & gap").File("DropdownOffsetExample.cs").Demo(DropdownOffsetExample).Testid("ui-dropdown-offset")
                .Blurb("Gap is the distance from the button; Offset shifts the menu along it, back past its edge "
                       + "when negative."),
            UiDocsExample.Key("ui-dropdown-shortcuts").Heading("Keyboard hints").File("DropdownShortcutsExample.cs").Demo(DropdownShortcutsExample).Testid("ui-dropdown-shortcuts")
                .Blurb("Kbd shows a shortcut at the end of a row and Suffix a word; the page binds the key itself. A "
                       + "disabled row is shown and stepped over."),
            UiDocsExample.Key("ui-dropdown-checkbox").Heading("Checkbox items").File("DropdownCheckboxExample.cs").Demo(DropdownCheckboxExample).Testid("ui-dropdown-checkbox")
                .Blurb("Rows that are on or off: Value with OnChange, or Bind to a model's flag. A pick closes the "
                       + "menu, as in Flux."),
            UiDocsExample.Key("ui-dropdown-radio").Heading("Radio items").File("DropdownRadioExample.cs").Demo(DropdownRadioExample).Testid("ui-dropdown-radio")
                .Blurb("One of several, in a Ui.MenuRadioGroup whose type is the type of its values."),
            UiDocsExample.Key("ui-dropdown-groups").Heading("Groups").File("DropdownGroupsExample.cs").Demo(DropdownGroupsExample).Testid("ui-dropdown-groups")
                .Blurb("Separators set related rows apart."),
            UiDocsExample.Key("ui-dropdown-headings").Heading("Groups with headings").File("DropdownHeadingsExample.cs").Demo(DropdownHeadingsExample).Testid("ui-dropdown-headings")
                .Blurb("A Ui.MenuGroup with a Heading names the rows under it."),
            UiDocsExample.Key("ui-dropdown-keep-open").Heading("Keep open").File("DropdownKeepOpenExample.cs").Demo(DropdownKeepOpenExample).Testid("ui-dropdown-keep-open")
                .Blurb("KeepOpen on the menu keeps it up after every pick, for one someone ticks several of. On the "
                       + "rows alone, the other rows still close it."),
            UiDocsExample.Key("ui-dropdown-open").Heading("Open state").File("DropdownOpenExample.cs").Demo(DropdownOpenExample).Testid("ui-dropdown-open")
                .Blurb("Open hands the state to the page, and OnToggle tells it when the reader opens or closes "
                       + "it. Unset, the browser keeps it and nothing is sent."),
            UiDropdownBehaviour
        ];
}
