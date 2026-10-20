using System.Linq.Expressions;
using Rask.Core;
using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>The reference tables of the UI tab's component pages: each part, and the steps it has of its own.</summary>
/// <remarks>
///     Written from the components' own doc comments. <c>UiApiReferenceTests</c> fails when a component gains a step
///     that is not listed here, or loses one that is.
/// </remarks>
internal static class UiApi
{
    private const string Hue = "Ui.Color: one of Tailwind's hues, each a step too (.Blue)";

    private const string Handler = "a handler, with no argument";

    private const string Toggled = "a handler given the state it is now in: open => …";

    private const string Picked = "a handler given the new value";

    private const string Selector = "a selector: (Visit v) => v.Visitors";

    private const string FormatText = "a UiChartFormat: new() { Style = Ui.ChartFormatStyle.Currency, Currency = \"USD\" }";

    public static IReadOnlyList<UiApiPart> Badge { get; } =
    [
        new("Ui.Badge", typeof(UiBadge), "flux:badge", "A status, a category or a count. Its children are its words.",
        [
            new("Color", typeof(Ui.Color?), "Its colour. Zinc when unset.", Hue),
            new("Size", typeof(Ui.BadgeSize?), "How big it is. Base when unset."),
            new("Variant", typeof(Ui.BadgeVariant?), "How it is filled. Soft when unset."),
            new("Rounded", typeof(bool?), "Rounds the ends fully, with the wider padding a round end needs."),
            new("Icon", typeof(Ui.IconName?), "An icon before the words."),
            new("IconTrailing", typeof(Ui.IconName?), "An icon after the words."),
            new("IconVariant", typeof(Ui.IconVariant?), "Which drawing of the icons. Micro, the size of the line, when unset."),
            new("As", typeof(Ui.BadgeAs?), "The element it is: a button to press with OnClick. A div when unset."),
            new("Inset", typeof(Ui.Inset?), "The sides whose padding is taken back, for a badge in a line of text."),
        ]),
        new("Ui.BadgeClose", typeof(UiBadgeClose), "flux:badge.close", "The button inside a badge that removes it. Its OnClick does the removing.",
        [
            new("Icon", typeof(Ui.IconName?), "The icon it shows. XMark when unset."),
            new("IconVariant", typeof(Ui.IconVariant?), "Which drawing of the icon. Micro when unset."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Button { get; } =
    [
        new("Ui.Button", typeof(UiButton), "flux:button", "A <button>, or an <a> given Href. Its children are its label.",
        [
            new("Variant", typeof(Ui.ButtonVariant?), "How it is drawn. Outline when unset."),
            new("Size", typeof(Ui.ButtonSize?), "How large it is. Base, 40px tall, when unset."),
            new("Color", typeof(Ui.Color?), "A hue for every variant but Danger; a gray changes only Primary.", Hue),
            new("Icon", typeof(Ui.IconName?), "The icon before the label, or with no label the whole button."),
            new("IconTrailing", typeof(UiInputIcon?), "What stands after the label: an icon, or content of your own.", "Ui.IconName, or a component"),
            new("IconVariant", typeof(Ui.IconVariant?), "Which drawing of the icons. Micro beside a label, mini alone, when unset."),
            new("Square", typeof(bool?), "As wide as it is tall. Automatic for a button with no children."),
            new("Align", typeof(Ui.Align?), "Where the content sits in a button wider than it. Centred when unset."),
            new("Inset", typeof(Ui.Inset?), "The sides to pull outwards, so a ghost button lines up with the text around it."),
            new("Loading", typeof(bool?), "Whether it shows that it waits. Unset, it does so while its own handler runs past 200 ms."),
            new("Tooltip", typeof(string), "A hint shown on hover and focus, which also names a button with no label."),
            new("TooltipPosition", typeof(Ui.TooltipPosition?), "Which side the tooltip opens on. Above when unset."),
            new("TooltipKbd", typeof(string), "A shortcut shown at the end of the tooltip: \"⌘S\"."),
            new("Kbd", typeof(string), "A shortcut shown inside the button, after its label."),
            new("Href", typeof(RouteUrl?), "Where it goes. A generated route stays in the app; a string is a link the browser follows.", "a route, or a URL string"),
            new("Type", typeof(Ui.ButtonType?), "What a press does: Submit on a form's submit button. Button when unset."),
            new("As", typeof(Ui.ButtonAs?), "The element to render. Href makes it an <a> without this."),
            new("Disabled", typeof(bool?), "Disables it by attribute, so the browser refuses the press."),
            new("Command", typeof(string), "The invoker command run on CommandFor's element: a dialog or popover opened with no script."),
            new("CommandFor", typeof(string), "The id of the element Command acts on."),
        ]),
        new("Ui.ButtonGroup", typeof(UiButtonGroup), "flux:button.group", "Fuses the buttons inside it into one control.", []),
    ];

    public static IReadOnlyList<UiApiPart> Callout { get; } =
    [
        new("Ui.Callout", typeof(UiCallout), "flux:callout", "A message set apart from the page: an icon, a heading, text and actions.",
        [
            new("Variant", typeof(Ui.CalloutVariant?), "What it is saying. Secondary on a white surface when unset."),
            new("Color", typeof(Ui.Color?), "A hue of its own, which wins over Variant.", Hue),
            new("Icon", typeof(Ui.IconName?), "The icon beside the content. None when unset."),
            new("IconVariant", typeof(Ui.IconVariant?), "Which drawing of the icon. Mini when unset."),
            new("CustomIcon", typeof(Component), "An icon of your own in place of Icon."),
            new("Heading", typeof(string), "Shorthand for a Ui.CalloutHeading as the first child."),
            new("Text", typeof(string), "Shorthand for a Ui.CalloutText after the heading."),
            new("Actions", typeof(Component), "What the reader can do about it: buttons or links."),
            new("Inline", typeof(bool?), "Puts the actions beside the text where there is room."),
            new("Controls", typeof(Component), "What sits at the top right: a dismiss button. Dismissing is the page's."),
            new("Role", typeof(string), "The callout's role. None when unset: a callout that appears because something happened says status or alert."),
        ]),
        new("Ui.CalloutHeading", typeof(UiCalloutHeading), "flux:callout.heading", "The callout's title line.",
        [
            new("Icon", typeof(Ui.IconName?), "An icon in the heading's line, instead of beside the whole callout."),
            new("IconVariant", typeof(Ui.IconVariant?), "Which drawing of the icon. Mini when unset."),
            new("CustomIcon", typeof(Component), "An icon of your own in place of Icon."),
        ]),
        new("Ui.CalloutText", typeof(UiCalloutText), "flux:callout.text", "The callout's body copy.", []),
        new("Ui.CalloutLink", typeof(UiCalloutLink), "flux:callout.link", "A link inside the text, drawn in the callout's ink.",
        [
            new("Href", typeof(RouteUrl?), "Where it goes: a generated route to stay in the app, or a URL string to leave it.", "a route, or a URL string"),
            new("External", typeof(bool?), "Opens it in a new tab, safely. Ignored for a route of the app."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Card { get; } =
    [
        new("Ui.Card", typeof(UiCard), "flux:card", "A container for related content, with the spacing between its parts.",
        [
            new("Variant", typeof(Ui.CardVariant?), "The card's surface."),
            new("Size", typeof(Ui.CardSize?), "Scales the padding, the corners and the spacing."),
            new("Body", typeof(Ui.CardBodyVariant?), "How the header, body and footer are set apart. Seamless when unset."),
            new("Divider", typeof(Ui.CardDivider?), "With Divided, where the lines stop."),
            new("Highlight", typeof(bool?), "A faint highlight inside the top edge in light mode. On unless false."),
        ]),
        new("Ui.CardHeader", typeof(UiCardHeader), "flux:card.header", "The heading, subheading and actions across the top.",
        [
            new("Size", typeof(Ui.CardSize?), "Outside a card, the size of the card it sits beside."),
        ]),
        new("Ui.CardHeading", typeof(UiCardHeading), "flux:card.heading", "The card's title.",
        [
            new("Size", typeof(Ui.CardHeadingSize?), "How big it looks."),
            new("Level", typeof(int?), "The heading level to render: 2 for an <h2>."),
        ]),
        new("Ui.CardSubheading", typeof(UiCardSubheading), "flux:card.subheading", "The line under the heading.", []),
        new("Ui.CardActions", typeof(UiCardActions), "flux:card.actions", "Buttons at the end of a header or footer.", []),
        new("Ui.CardBody", typeof(UiCardBody), "flux:card.body", "The card's content.", []),
        new("Ui.CardFooter", typeof(UiCardFooter), "flux:card.footer", "The line along the bottom.",
        [
            new("Size", typeof(Ui.CardSize?), "Outside a card, the size of the card it sits beside."),
        ]),
        new("Ui.CardBleed", typeof(UiCardBleed), "flux:card.bleed", "Content that runs to the card's edges: an image.", []),
    ];

    public static IReadOnlyList<UiApiPart> Command { get; } =
    [
        new("Ui.Command", typeof(UiCommand), "flux:command", "The palette: a box to type in over a list of commands, narrowed in the browser.", []),
        new("Ui.CommandInput", typeof(UiCommandInput), "flux:command.input", "The box that narrows the list.",
        [
            new("Placeholder", typeof(string), "What the box says while it is empty."),
            new("Icon", typeof(Ui.IconName?), "The icon at the start of the box. A magnifying glass when unset."),
            new("Clearable", typeof(bool?), "A button that empties the box while it holds something. On unless false."),
            new("Closable", typeof(bool?), "A button that closes the modal the palette is in."),
        ]),
        new("Ui.CommandItems", typeof(UiCommandItems), "flux:command.items", "The list of commands. It says \"No results found\" when typing hides them all.", []),
        new("Ui.CommandItem", typeof(UiCommandItem), "flux:command.item", "One command. Its children are its words, which the box matches.",
        [
            new("Icon", typeof(Ui.IconName?), "The icon at the start of the row."),
            new("IconVariant", typeof(Ui.IconVariant?), "Which drawing of the icon. Outline when unset."),
            new("Kbd", typeof(string), "A shortcut shown at the end of the row: \"⌘K\". A hint: the page binds the key."),
            new("OnClick", typeof(Callback), "What pressing the command, or Enter on it, does.", Handler),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Context { get; } =
    [
        new("Ui.Context", typeof(UiContext), "flux:context", "Opens its second child, a Ui.Menu, at the pointer when its first child is right-clicked.",
        [
            new("Position", typeof(Ui.ContextPosition?), "Where the menu opens against the pointer. Below it, its end edge on it, when unset."),
            new("Gap", typeof(int?), "The distance between the pointer and the menu, in pixels."),
            new("Offset", typeof((int X, int Y)?), "Shifts the menu from where it would open, in pixels.", "(int X, int Y)"),
            new("Target", typeof(string), "The id of a Ui.Menu written somewhere else, for one that cannot sit inside."),
            new("Detail", typeof(string), "A value for the menu's data-detail, to tell which area opened it."),
            new("Disabled", typeof(bool?), "Leaves a right-click to the browser's own menu."),
            new("Open", typeof(bool?), "Hands the open state to the page. Unset, the reader opens and closes it."),
            new("OnToggle", typeof(Callback<bool>), "Runs when the reader opens or closes it.", Toggled),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Dropdown { get; } =
    [
        new("Ui.Dropdown", typeof(UiDropdown), "flux:dropdown", "A trigger, its first child, and what it opens, its second: a menu, a navigation menu or a popover.",
        [
            new("Position", typeof(Ui.DropdownPosition?), "Which side of the trigger it opens on. Below when unset."),
            new("Align", typeof(Ui.DropdownAlign?), "Where along that side it sits. Its start edge on the trigger's when unset."),
            new("Gap", typeof(int?), "The distance from the trigger, in pixels. 5 when unset."),
            new("Offset", typeof(int?), "Shifts it along its alignment, in pixels; negative moves it back past the trigger's edge."),
            new("Hover", typeof(bool?), "Opens it while the pointer is over the trigger or the panel. A press and the keyboard still do."),
            new("Open", typeof(bool?), "Hands the open state to the page. Unset, the reader opens and closes it."),
            new("OnToggle", typeof(Callback<bool>), "Runs when the reader opens or closes it.", Toggled),
        ]),
        new("Ui.Menu", typeof(UiMenu), "flux:menu", "The rows a dropdown or a context menu opens, with Flux's keyboard.",
        [
            new("KeepOpen", typeof(bool?), "Keeps it open after any row in it is picked."),
        ]),
        new("Ui.MenuItem", typeof(UiMenuItem), "flux:menu.item", "An action. Its children are its words.",
        [
            new("Icon", typeof(Ui.IconName?), "An icon at the start of the row."),
            new("IconTrailing", typeof(Ui.IconName?), "An icon at the end of the row."),
            new("IconVariant", typeof(Ui.IconVariant?), "Which drawing of the icons. Mini, 20px, when unset."),
            new("Kbd", typeof(string), "A shortcut shown at the end of the row: \"⌘S\". A hint: the page binds the key."),
            new("Suffix", typeof(string), "Words at the end of the row: a count, a state."),
            new("Variant", typeof(Ui.MenuItemVariant?), "Danger for a destructive action."),
            new("Disabled", typeof(bool?), "Shown but not pickable. The keyboard steps over it."),
            new("KeepOpen", typeof(bool?), "Keeps the menu open after this row is picked."),
            new("Href", typeof(RouteUrl?), "Where it leads. A generated route stays in the app; a string is a link the browser follows.", "a route, or a URL string"),
            new("OnClick", typeof(Callback), "What picking it does.", Handler),
        ]),
        new("Ui.MenuSubmenu", typeof(UiMenuSubmenu), "flux:menu.submenu", "A row that flies out a menu of its own beside it.",
        [
            new("Heading", typeof(string), "The row's words. Required, and taken first."),
            new("Icon", typeof(Ui.IconName?), "An icon at the start of the row."),
            new("IconTrailing", typeof(Ui.IconName?), "The icon at the end of the row. A chevron when unset."),
            new("IconVariant", typeof(Ui.IconVariant?), "Which drawing of the icons. Mini when unset."),
            new("KeepOpen", typeof(bool?), "Keeps the menu open after any row in it is picked."),
        ]),
        new("Ui.MenuSeparator", typeof(UiMenuSeparator), "flux:menu.separator", "A line between rows.", []),
        new("Ui.MenuGroup", typeof(UiMenuGroup), "flux:menu.group", "Rows set apart from the rest, under a heading or between lines.",
        [
            new("Heading", typeof(string), "The words above the rows. Without it the group is set apart by lines alone."),
        ]),
        new("Ui.MenuCheckbox", typeof(UiMenuCheckbox), "flux:menu.checkbox", "A row that is on or off.",
        [
            new("Bind", typeof(Expression<Func<bool>>), "The model's flag it reads and writes: () => m.Draft. Or Value with OnChange.", "() => a bool"),
            new("Value", typeof(bool), "Whether it is ticked, for a page that holds the flag itself."),
            new("OnChange", typeof(Callback<bool>), "Runs with the new state when it is picked.", Picked),
            new("AfterBind", typeof(Callback<bool>), "Runs after Bind has written the model.", Picked),
            new("Validate", typeof(Validator<bool>?), "A rule the value is checked by when it is written.", "a validator"),
            new("Live", typeof(bool?), "Writes the model as soon as it is picked, Flux's wire:model.live, not with the next action."),
            new("Disabled", typeof(bool?), "Shown but not pickable."),
            new("KeepOpen", typeof(bool?), "Keeps the menu open after it is picked."),
        ]),
        new("Ui.MenuCheckboxGroup", typeof(UiMenuCheckboxGroup), "flux:menu.checkbox.group", "Checkbox rows kept together. Each binds its own flag.", []),
        new("Ui.MenuRadioGroup", typeof(UiMenuRadioGroup<string>), "flux:menu.radio.group", "Radio rows of which one is picked. Its type is the type of their values.",
        [
            new("Bind", typeof(Expression<Func<string>>), "The model's value it reads and writes: () => m.Sort. Or Value with OnChange.", "() => a T"),
            new("Value", typeof(string), "The picked value, for a page that holds it itself.", "T, the type of the radios' values"),
            new("OnChange", typeof(Callback<string>), "Runs with the new value when a row is picked.", Picked),
            new("AfterBind", typeof(Callback<string>), "Runs after Bind has written the model.", Picked),
            new("Validate", typeof(Validator<string>?), "A rule the value is checked by when it is written.", "a validator"),
            new("Live", typeof(bool?), "Writes the model as soon as a row is picked, not with the next action."),
            new("KeepOpen", typeof(bool?), "Keeps the menu open after a row in it is picked."),
        ]),
        new("Ui.MenuRadio", typeof(UiMenuRadio), "flux:menu.radio", "One choice in a radio group.",
        [
            new("Value", typeof(object), "The value it stands for, compared with the group's.", "a value of the group's type"),
            new("Checked", typeof(bool?), "Whether it is the chosen one, for a radio outside a group."),
            new("Disabled", typeof(bool?), "Shown but not pickable."),
            new("KeepOpen", typeof(bool?), "Keeps the menu open after it is picked."),
            new("OnClick", typeof(Callback), "Runs when it is picked, after its group has taken the value.", Handler),
        ]),
        new("Ui.Navmenu", typeof(UiNavmenu), "flux:navmenu", "A dropdown's list of links: no menu roles, no cursor, and Tab walks it.", []),
        new("Ui.NavmenuItem", typeof(UiNavmenuItem), "flux:navmenu.item", "One link. Its children are its words.",
        [
            new("Href", typeof(RouteUrl?), "Where it leads. Without one the row is a button.", "a route, or a URL string"),
            new("OnClick", typeof(Callback), "What pressing a row with no Href does.", Handler),
            new("Icon", typeof(Ui.IconName?), "An icon at the start of the row."),
            new("IconVariant", typeof(Ui.IconVariant?), "Which drawing of the icon. Mini when unset."),
            new("Variant", typeof(Ui.MenuItemVariant?), "Danger for a destructive destination."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Heading { get; } =
    [
        new("Ui.Heading", typeof(UiHeading), "flux:heading", "A title. Its size and its place in the outline are separate.",
        [
            new("Size", typeof(Ui.HeadingSize?), "How big it looks. Base when unset."),
            new("Level", typeof(int?), "The heading level, 1 to 4. Unset, it is a <div> outside the outline."),
            new("Accent", typeof(bool?), "Draws it in the accent colour."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Icon { get; } =
    [
        new("Ui.Icon", typeof(UiIcon), "flux:icon.*", "One of the Heroicons, as an inline SVG.",
        [
            new("Name", typeof(Ui.IconName), "Which icon to draw. Required, and taken first."),
            new("Variant", typeof(Ui.IconVariant?), "Which drawing of it, and so how large. Outline when unset."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Modal { get; } =
    [
        new("Ui.Modal", typeof(UiModal), "flux:modal", "A real modal <dialog>: the top layer, an inert page behind it, Escape, and focus handed back.",
        [
            new("Name", typeof(string), "What a Ui.ModalTrigger opens it by. Unique on the page: it is the dialog's id."),
            new("Flyout", typeof(bool?), "Anchors it to an edge of the viewport at full height, for longer forms."),
            new("Position", typeof(Ui.ModalPosition?), "The edge a flyout opens from. Right when unset."),
            new("Variant", typeof(Ui.ModalVariant?), "How it is drawn: the panel, a floating flyout, or nothing around the content."),
            new("Scroll", typeof(Ui.ModalScroll?), "What scrolls when it is taller than the viewport: the dialog, or the whole layer."),
            new("Dismissible", typeof(bool?), "Whether a click outside closes it. On unless false."),
            new("Escapable", typeof(bool?), "Whether Escape closes it. On unless false."),
            new("Closable", typeof(bool?), "Whether it shows the close button in its corner. On unless false."),
            new("Open", typeof(bool?), "Hands the open state to the page, Flux's wire:model. Unset, its triggers open it."),
            new("OnClose", typeof(Callback), "Runs when it closes, by any means: where a page that set Open hears it.", Handler),
            new("OnCancel", typeof(Callback), "Runs when it is dismissed, by Escape or a click outside, before OnClose.", Handler),
        ]),
        new("Ui.ModalTrigger", typeof(UiModalTrigger), "flux:modal.trigger", "Makes the button inside it open the modal of that name, with no handler.",
        [
            new("Name", typeof(string), "The Name of the modal it opens. Required, and taken first."),
            new("Shortcut", typeof(string), "Keys that open it from anywhere on the page: \"mod+k\" is ⌘K on a Mac and Ctrl K elsewhere."),
        ]),
        new("Ui.ModalClose", typeof(UiModalClose), "flux:modal.close", "Makes the button inside it close the modal it is in: a close, not a dismissal.", []),
    ];

    public static IReadOnlyList<UiApiPart> Popover { get; } =
    [
        new("Ui.Popover", typeof(UiPopover), "flux:popover", "The panel a Ui.Dropdown opens beside its trigger, for what is not a menu. Where it opens is the dropdown's.", []),
    ];

    public static IReadOnlyList<UiApiPart> Progress { get; } =
    [
        new("Ui.Progress", typeof(UiProgress), "flux:progress", "A <div role=\"progressbar\">: a 6px track the width of its container and the bar filling it.",
        [
            new("Value", typeof(double?), "How far along it is, from 0 to Max. 0 when unset."),
            new("Max", typeof(double?), "What Value is out of. 100 when unset."),
            new("Color", typeof(Ui.Color?), "The bar's colour. The accent when unset.", Hue),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Skeleton { get; } =
    [
        new("Ui.Skeleton", typeof(UiSkeleton), "flux:skeleton", "A block in the shape of what is coming: 16px tall and as wide as its container until Class sizes and rounds it.",
        [
            new("Animate", typeof(Ui.SkeletonAnimate?), "How it shows something is on its way. Its group's when unset, and still without one."),
            new("Style", typeof(string), "Inline CSS for the block: a width known only at run time."),
        ]),
        new("Ui.SkeletonLine", typeof(UiSkeletonLine), "flux:skeleton.line", "A line of text that has not arrived: the line's full height, a bar the height of its letters.",
        [
            new("Size", typeof(Ui.SkeletonLineSize?), "The size of text it stands in for. Base when unset."),
            new("Animate", typeof(Ui.SkeletonAnimate?), "How it shows something is on its way. Its group's when unset, and still without one."),
            new("Style", typeof(string), "Inline CSS for the line: a width known only at run time."),
        ]),
        new("Ui.SkeletonGroup", typeof(UiSkeletonGroup), "flux:skeleton.group", "A <div> that draws nothing and animates every skeleton inside it, however deep.",
        [
            new("Animate", typeof(Ui.SkeletonAnimate?), "How every skeleton inside animates, unless it states its own."),
            new("Style", typeof(string), "Inline CSS for the group."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Toast { get; } =
    [
        new("Ui.Toast", typeof(UiToast), "flux:toast", "Where the app's toasts appear. Placed once, in the layout; a toast is raised with Toast.Success(\"Saved\").",
        [
            new("Position", typeof(Ui.ToastPosition?), "The corner they appear in. The bottom end when unset; a group around it decides instead."),
            new("Invert", typeof(bool?), "Dark in light mode and light in dark, for every toast."),
        ]),
        new("Ui.ToastGroup", typeof(UiToastGroup), "flux:toast.group", "Wrapped around a Ui.Toast, toasts stack instead of taking one another's place.",
        [
            new("Position", typeof(Ui.ToastPosition?), "The corner the stack sits in. The bottom end when unset."),
            new("Expanded", typeof(bool?), "Lays every toast out at all times, not only under the pointer."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Tooltip { get; } =
    [
        new("Ui.Tooltip", typeof(UiTooltip), "flux:tooltip", "A line of help beside its first child, the trigger, while that is hovered or has keyboard focus.",
        [
            new("Content", typeof(string), "The text it shows. For more, write a Ui.TooltipContent after the trigger."),
            new("Position", typeof(Ui.TooltipPosition?), "Which side of the trigger it opens on. Top when unset."),
            new("Align", typeof(Ui.TooltipAlign?), "Where along that side it sits. Centred when unset."),
            new("Gap", typeof(int?), "The distance from the trigger, in pixels. 5 when unset."),
            new("Offset", typeof(int?), "How far it slides along its side, in pixels, away from the edge it is aligned to."),
            new("Kbd", typeof(string), "A shortcut shown after Content: \"⌘S\"."),
            new("Disabled", typeof(bool?), "Never shows. The trigger keeps its description."),
            new("Toggleable", typeof(bool?), "Opens on a click rather than a hover: the one tooltip a touch screen shows."),
            new("Interactive", typeof(bool?), "Its content is more than a description, so the trigger controls it rather than being described by it."),
        ]),
        new("Ui.TooltipContent", typeof(UiTooltipContent), "flux:tooltip.content", "What a tooltip shows when it is more than a line of text. Class takes a max-w-* or a space-y-*.",
        [
            new("Kbd", typeof(string), "A shortcut shown after the content: \"⌘S\"."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Separator { get; } =
    [
        new("Ui.Separator", typeof(UiSeparator), "flux:separator", "A line between sections, or between items in a row. It carries no margin.",
        [
            new("Vertical", typeof(bool?), "Runs down instead of across."),
            new("Orientation", typeof(Ui.SeparatorOrientation?), "The other way to say Vertical, for a direction decided at run time."),
            new("Variant", typeof(Ui.SeparatorVariant?), "How strongly the line is drawn."),
            new("Text", typeof(string), "A word in the middle of the line: \"or\"."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Text { get; } =
    [
        new("Ui.Text", typeof(UiText), "flux:text", "Body copy: a paragraph, or a span with Inline.",
        [
            new("Size", typeof(Ui.TextSize?), "How big it looks. Default when unset."),
            new("Variant", typeof(Ui.TextVariant?), "How much it stands out. Default when unset."),
            new("Color", typeof(Ui.Color?), "One of Tailwind's hues in place of the variant's ink.", Hue),
            new("Inline", typeof(bool?), "A <span> rather than a paragraph."),
        ]),
        new("Ui.Link", typeof(UiLink), "flux:link", "A link in the accent colour, underlined.",
        [
            new("Href", typeof(RouteUrl?), "Where it goes: a generated route to stay in the app, or a URL string to leave it.", "a route, or a URL string"),
            new("Variant", typeof(Ui.LinkVariant?), "How it shows that it is a link. Default when unset."),
            new("External", typeof(bool?), "Opens it in a new tab with rel=\"noopener noreferrer\". Ignored for a route."),
            new("As", typeof(Ui.LinkAs?), "The element it renders: a <button> drawn as a link. A when unset."),
            new("Accent", typeof(bool?), "Whether it takes the accent colour. On unless false."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Accordion { get; } =
    [
        new("Ui.Accordion", typeof(UiAccordion), "flux:accordion", "A stack of items, each a heading that opens the content under it.",
        [
            new("Variant", typeof(Ui.AccordionVariant?), "Which side of the heading the chevron is on. After it when unset."),
            new("Transition", typeof(bool?), "Opens and closes items over a quarter of a second rather than at once."),
            new("Exclusive", typeof(bool?), "Opening one item closes the others: the platform's own <details name> group."),
        ]),
        new("Ui.AccordionItem", typeof(UiAccordionItem), "flux:accordion.item", "One item: a <details>, its heading the <summary>.",
        [
            new("Heading", typeof(string), "The heading's words, in place of a Ui.AccordionHeading child; the children are then the content."),
            new("Expanded", typeof(bool?), "Open to begin with. The browser owns it from there."),
            new("Disabled", typeof(bool?), "Cannot be opened or closed, and leaves the tab order."),
        ]),
        new("Ui.AccordionHeading", typeof(UiAccordionHeading), "flux:accordion.heading", "The line that opens and closes the item, with its chevron.", []),
        new("Ui.AccordionContent", typeof(UiAccordionContent), "flux:accordion.content", "What the item opens to show.", []),
    ];

    public static IReadOnlyList<UiApiPart> Avatar { get; } =
    [
        new("Ui.Avatar", typeof(UiAvatar), "flux:avatar", "A person as a picture, or as initials or an icon when there is none. Children replace all of it: \"3+\".",
        [
            new("Name", typeof(string), "Whose it is: the source of the initials, the picture's alternative text and the tooltip's words."),
            new("Src", typeof(string), "The picture's address. Initials or an icon when unset."),
            new("Initials", typeof(string), "The initials to show, rather than those worked out from Name."),
            new("InitialsSingle", typeof(bool?), "One letter rather than two."),
            new("Alt", typeof(string), "The picture's alternative text. Name when unset."),
            new("Size", typeof(Ui.AvatarSize?), "How large it is. 40px when unset."),
            new("Color", typeof(Ui.Color?), "The fill behind initials or an icon. Zinc when unset.", Hue),
            new("ColorAuto", typeof(bool?), "Picks the fill from the initials, so one person is one colour everywhere: Flux's color=\"auto\"."),
            new("ColorSeed", typeof(string), "What ColorAuto picks from instead of the initials: an id that never changes."),
            new("Circle", typeof(bool?), "A circle rather than a rounded square."),
            new("Icon", typeof(Ui.IconName?), "An icon in place of initials."),
            new("IconVariant", typeof(Ui.IconVariant?), "Which drawing of the icon. Solid when unset."),
            new("Tooltip", typeof(string), "Words shown on hover and keyboard focus."),
            new("TooltipPosition", typeof(Ui.TooltipPosition?), "Which side the tooltip opens on. Above when unset."),
            new("Badge", typeof(Component), "A mark on one corner: a count, an emoji, any content. \"\" is the plain dot.", "a string, or a component"),
            new("BadgeColor", typeof(Ui.Color?), "The badge's colour. The page's own ground when unset.", Hue),
            new("BadgeCircle", typeof(bool?), "A round badge rather than a slightly rounded one."),
            new("BadgePosition", typeof(Ui.AvatarBadgePosition?), "Which corner the badge sits on. Bottom right when unset."),
            new("BadgeVariant", typeof(Ui.AvatarBadgeVariant?), "How the badge is drawn."),
            new("As", typeof(Ui.AvatarAs?), "The element it is written as, when it is not a link: a button to press."),
            new("Href", typeof(RouteUrl?), "Where it goes, which makes it a link. A generated route stays in the app.", "a route, or a URL string"),
        ]),
        new("Ui.AvatarGroup", typeof(UiAvatarGroup), "flux:avatar.group", "Stacks avatars, each over the one before and ringed in the page's colour; Class gives another ring.", []),
    ];

    public static IReadOnlyList<UiApiPart> Table { get; } =
    [
        new("Ui.Table", typeof(UiTable), "flux:table", "A <table> in a box that scrolls, with its pager under the rows. It sorts and pages nothing itself.",
        [
            new("Bleed", typeof(bool?), "Runs the row dividers through the padding of the box it sits in, by --ui-bleed (1.5rem unless the box says)."),
            new("Paginate", typeof(Component), "The pager under the rows, outside the scroll area: a Ui.Pagination.", "a Ui.Pagination"),
            new("ContainerClass", typeof(string), "Classes for the box around the table: a height to scroll within, max-h-80, or min-w-0 in a grid."),
        ]),
        new("Ui.TableColumns", typeof(UiTableColumns), "flux:table.columns", "The heading row: the <thead>.",
        [
            new("Sticky", typeof(bool?), "Keeps the headings in view while the rows scroll under them. Give it a background too."),
        ]),
        new("Ui.TableColumn", typeof(UiTableColumn), "flux:table.column", "One heading: the <th>. Its children are its label.",
        [
            new("Align", typeof(Ui.Align?), "Where the heading sits in its column. Start when unset."),
            new("Sortable", typeof(bool?), "Makes the heading a button with a chevron that asks for this column to be sorted."),
            new("Sorted", typeof(bool?), "Whether the rows are sorted by this column now: its chevron stays visible."),
            new("Direction", typeof(Ui.TableColumnDirection?), "Which way the sorted column runs. Ascending when unset."),
            new("Sticky", typeof(bool?), "Keeps the column in view while the others scroll sideways. Its cells say Sticky too, and all of it a background."),
            new("OnSort", typeof(Callback), "Runs when a sortable heading is clicked, or its button pressed from the keyboard.", Handler),
        ]),
        new("Ui.TableRows", typeof(UiTableRows), "flux:table.rows", "The body: the <tbody>, holding a row per record.", []),
        new("Ui.TableRow", typeof(UiTableRow), "flux:table.row", "One record: the <tr>. Key it with the record's id.",
        [
            new("Sticky", typeof(bool?), "Keeps the row in view while the others scroll under it."),
        ]),
        new("Ui.TableCell", typeof(UiTableCell), "flux:table.cell", "One value: the <td>.",
        [
            new("Align", typeof(Ui.Align?), "Where the value sits in its column. Start when unset."),
            new("Variant", typeof(Ui.TableCellVariant?), "How much the cell stands out. Muted when unset."),
            new("Sticky", typeof(bool?), "Keeps the cell in view while the others scroll sideways. Give it a background too."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Chart { get; } =
    [
        new("Ui.Chart", typeof(UiChart), "flux:chart", "The chart: its rows, and the parts that draw them. Sized by its class, aspect-3/1 or h-64.",
        [
            new("Value", typeof(UiChartData), "The rows, one point, bar or slice each: Flux's wire:model and :value.", "a list of rows, or of numbers"),
            new("Horizontal", typeof(bool?), "Puts the rows down the Y axis and their values along the X axis: bars on their side."),
        ]),
        new("Ui.ChartSvg", typeof(UiChartSvg), "flux:chart.svg", "The drawing: the lines, areas, points, bars, pies and axes inside it.",
        [
            new("Gutter", typeof(string), "The space kept clear inside each edge, as a CSS padding: \"12 0 12 8\". 8 all round when unset; \"0\" for a sparkline."),
        ]),
        new("Ui.ChartViewport", typeof(UiChartViewport), "flux:chart.viewport", "The drawing's own box, when a legend or a summary shares the chart.", []),
        new("Ui.ChartLine", typeof(UiChartLine), "flux:chart.line", "A line through one value of every row.",
        [
            new("Field", typeof(UiChartField), "What it reads from a row. The row itself when the chart holds bare numbers.", Selector),
            new("Curve", typeof(Ui.ChartCurve?), "How the points are joined. Smooth when unset."),
            new("StrokeDasharray", typeof(string), "The line's dashes: \"4 4\"."),
        ]),
        new("Ui.ChartArea", typeof(UiChartArea), "flux:chart.area", "The area under a line, filled.",
        [
            new("Field", typeof(UiChartField), "What it reads from a row. The row itself when the chart holds bare numbers.", Selector),
            new("Curve", typeof(Ui.ChartCurve?), "How the points are joined. Smooth when unset."),
        ]),
        new("Ui.ChartPoint", typeof(UiChartPoint), "flux:chart.point", "A dot on every row's value. The hovered row's carry data-active.",
        [
            new("Field", typeof(UiChartField), "What it reads from a row.", Selector),
            new("R", typeof(double?), "The dot's radius, in pixels. 4 when unset."),
            new("StrokeWidth", typeof(double?), "The ring in the surface colour that lifts a dot off its line."),
        ]),
        new("Ui.ChartBar", typeof(UiChartBar), "flux:chart.bar", "A bar for every row: alone, or in a group or a stack.",
        [
            new("Field", typeof(UiChartField), "What it reads from a row.", Selector),
            new("Radius", typeof(string), "How round its corners are: \"4\", or \"4 0\" for the value end and then the base."),
            new("Width", typeof(string), "How much of its slot it fills, as a percentage or in pixels: \"85%\"."),
        ]),
        new("Ui.ChartGroup", typeof(UiChartGroup), "flux:chart.group", "Bars side by side in each row's slot.",
        [
            new("Width", typeof(string), "How much of a row's slot the group fills: \"90%\"."),
        ]),
        new("Ui.ChartStack", typeof(UiChartStack), "flux:chart.stack", "Bars one on top of another in each row's slot.",
        [
            new("Width", typeof(string), "How much of a row's slot the stack fills: \"65%\"."),
        ]),
        new("Ui.ChartPie", typeof(UiChartPie), "flux:chart.pie", "A slice per row, or a donut. The hovered slice carries data-active, the others data-inactive.",
        [
            new("Field", typeof(UiChartField), "The number that sets each slice's size.", Selector),
            new("LabelField", typeof(UiChartField), "What each slice is called, in the tooltip.", Selector),
            new("ColorField", typeof(UiChartField), "A Ui.Color each row chooses for its slice. The built-in palette in turn when unset.", Selector),
            new("InnerRadius", typeof(string), "The donut's hole, in pixels or as a share of the radius: \"60%\"."),
            new("Radius", typeof(double?), "How round each slice's corners are, in pixels."),
        ]),
        new("Ui.ChartAxis", typeof(UiChartAxis), "flux:chart.axis", "An axis, holding its ticks, grid, line and marks. .X and .Y say which.",
        [
            new("Axis", typeof(Ui.ChartAxisAxis?), "Which axis it is."),
            new("Field", typeof(UiChartField), "What names each row along the index axis. A date or time makes it a time axis.", Selector),
            new("Format", typeof(UiChartFormat), "How the tick labels are written: Intl's options, under Intl's names.", FormatText),
            new("Position", typeof(Ui.Position?), "Which side of the plot it sits on: Right for a value axis on the right."),
            new("TickCount", typeof(int?), "About how many ticks. Five on a value axis; every row on a band axis."),
            new("TickStart", typeof(double?), "Where the value axis starts. Zero, or the lowest value below it, when unset."),
            new("TickEnd", typeof(double?), "Where the value axis ends. The tick just past the highest value when unset."),
            new("TickValues", typeof(IReadOnlyList<double>), "The ticks themselves, in place of the round ones the axis would choose.", "a list of numbers"),
            new("TickPrefix", typeof(string), "Written before every tick label: \"$\"."),
            new("TickSuffix", typeof(string), "Written after every tick label: \"MB\"."),
        ]),
        new("Ui.ChartAxisTick", typeof(UiChartAxisTick), "flux:chart.axis.tick", "The axis's labels.",
        [
            new("Format", typeof(UiChartFormat), "How they are written. The axis's own format when unset.", FormatText),
        ]),
        new("Ui.ChartAxisGrid", typeof(UiChartAxisGrid), "flux:chart.axis.grid", "A line across the plot at every tick.",
        [
            new("StrokeWidth", typeof(double?), "How thick the lines are."),
            new("StrokeDasharray", typeof(string), "Their dashes: \"4 4\"."),
        ]),
        new("Ui.ChartAxisLine", typeof(UiChartAxisLine), "flux:chart.axis.line", "The axis's own line along the plot's edge.",
        [
            new("StrokeWidth", typeof(double?), "How thick it is."),
        ]),
        new("Ui.ChartAxisMark", typeof(UiChartAxisMark), "flux:chart.axis.mark", "A short mark at every tick.",
        [
            new("Position", typeof(Ui.Position?), "Which side of the plot they sit on. The axis's own when unset."),
            new("StrokeWidth", typeof(double?), "How thick they are."),
        ]),
        new("Ui.ChartZeroLine", typeof(UiChartZeroLine), "flux:chart.zero-line", "A line at zero, where values run below it.",
        [
            new("StrokeWidth", typeof(double?), "How thick it is."),
        ]),
        new("Ui.ChartCursor", typeof(UiChartCursor), "flux:chart.cursor", "What marks the row under the pointer: one, moved by the browser.",
        [
            new("Type", typeof(Ui.ChartCursorType?), "A dashed line through the row, or its whole band shaded. A line when unset."),
            new("StrokeWidth", typeof(double?), "How thick the line is."),
            new("StrokeDasharray", typeof(string), "Its dashes: \"4 4\"."),
        ]),
        new("Ui.ChartTooltip", typeof(UiChartTooltip), "flux:chart.tooltip", "The box beside the row under the pointer: one, moved by the browser.",
        [
            new("Field", typeof(UiChartField), "The field it shows, when it holds no parts of its own.", Selector),
            new("Format", typeof(UiChartFormat), "How that field is written.", FormatText),
        ]),
        new("Ui.ChartTooltipHeading", typeof(UiChartTooltipHeading), "flux:chart.tooltip.heading", "The tooltip's first line: the row's name or date.",
        [
            new("Field", typeof(UiChartField), "What it shows.", Selector),
            new("Format", typeof(UiChartFormat), "How it is written.", FormatText),
        ]),
        new("Ui.ChartTooltipValue", typeof(UiChartTooltipValue), "flux:chart.tooltip.value", "A value of the row under the pointer, with its label.",
        [
            new("Field", typeof(UiChartField), "The number shown.", Selector),
            new("Format", typeof(UiChartFormat), "How it is written.", FormatText),
            new("Label", typeof(string), "What comes before the number: a series' name."),
            new("LabelField", typeof(UiChartField), "A label read from the row instead: the hovered slice's name, on a pie.", Selector),
            new("Prefix", typeof(string), "Written before the number: \"$\"."),
            new("Suffix", typeof(string), "Written after the number: \"%\"."),
        ]),
        new("Ui.ChartTooltipIndicator", typeof(UiChartTooltipIndicator), "flux:chart.tooltip.indicator", "A dot beside a value, in the series' colour or the hovered slice's.", []),
        new("Ui.ChartSummary", typeof(UiChartSummary), "flux:chart.summary", "Figures beside the chart that read the row under the pointer, and the latest row otherwise.", []),
        new("Ui.ChartSummaryValue", typeof(UiChartSummaryValue), "flux:chart.summary.value", "One figure of a summary.",
        [
            new("Field", typeof(UiChartField), "What it shows.", Selector),
            new("Fallback", typeof(string), "What it shows when there is no row to read."),
            new("Format", typeof(UiChartFormat), "How it is written.", FormatText),
        ]),
        new("Ui.ChartLegend", typeof(UiChartLegend), "flux:chart.legend", "One entry of a legend: a series' name and its indicator.",
        [
            new("Field", typeof(UiChartField), "The field the entry stands for.", Selector),
            new("Label", typeof(string), "The series' name."),
            new("Format", typeof(UiChartFormat), "How the field's value is written.", FormatText),
        ]),
        new("Ui.ChartLegendIndicator", typeof(UiChartLegendIndicator), "flux:chart.legend.indicator", "The legend entry's dot; its class gives the colour.", []),
    ];

    public static IReadOnlyList<UiApiPart> Kanban { get; } =
    [
        new("Ui.Kanban", typeof(UiKanban), "flux:kanban", "The row the columns stand in, 16px apart. It draws a board and moves nothing.", []),
        new("Ui.KanbanColumn", typeof(UiKanbanColumn), "flux:kanban.column", "One stage: a 320px tinted panel as tall as what is in it.", []),
        new("Ui.KanbanColumnHeader", typeof(UiKanbanColumnHeader), "flux:kanban.column.header", "The stage's name and what goes beside it. Children replace the heading and count.",
        [
            new("Heading", typeof(string), "What the column is called."),
            new("Subheading", typeof(string), "A second line under the heading."),
            new("Count", typeof(int?), "A number beside the heading: how many cards the column holds."),
            new("Actions", typeof(Component), "Buttons or a menu at the end of the heading's row."),
        ]),
        new("Ui.KanbanColumnCards", typeof(UiKanbanColumnCards), "flux:kanban.column.cards", "The column's cards, one under the other.", []),
        new("Ui.KanbanColumnFooter", typeof(UiKanbanColumnFooter), "flux:kanban.column.footer", "What sits under the cards: a New card button, a form.", []),
        new("Ui.KanbanCard", typeof(UiKanbanCard), "flux:kanban.card", "A card. Children replace the heading.",
        [
            new("Heading", typeof(string), "The card's title."),
            new("As", typeof(Ui.KanbanCardAs?), "The element it is: Button makes the whole card one to press. A div when unset."),
            new("Header", typeof(Component), "What sits above the heading: badges, tags."),
            new("Footer", typeof(Component), "What sits under the heading: an icon, avatars."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Timeline { get; } =
    [
        new("Ui.Timeline", typeof(UiTimeline), "flux:timeline", "Events or steps in order, with a line drawn between their indicators.",
        [
            new("Horizontal", typeof(bool?), "Across the page rather than down it: indicators in a row, the content under each."),
            new("Align", typeof(Ui.TimelineAlign?), "Where every item's content sits beside its indicator. Centred when unset."),
            new("Size", typeof(Ui.TimelineSize?), "The size of the indicators, and with it the line and the gaps."),
        ]),
        new("Ui.TimelineItem", typeof(UiTimelineItem), "flux:timeline.item", "One event: an indicator and its content, or a block.",
        [
            new("Status", typeof(Ui.TimelineStatus?), "How far along it is: it draws the indicator, and a complete item darkens the line on to the next."),
            new("Align", typeof(Ui.TimelineAlign?), "Where this item's content sits, when not where the timeline puts it."),
            new("Size", typeof(Ui.TimelineSize?), "The size of this item's indicator, when not the timeline's."),
        ]),
        new("Ui.TimelineIndicator", typeof(UiTimelineIndicator), "flux:timeline.indicator", "The circle on the line: an icon, a number, a word.",
        [
            new("Variant", typeof(Ui.TimelineIndicatorVariant?), "Bare drops the circle and its size, for a larger icon standing on its own."),
            new("Status", typeof(Ui.TimelineStatus?), "The status to draw, when not the item's."),
            new("Color", typeof(Ui.Color?), "A coloured circle. A status is drawn instead of it.", Hue),
        ]),
        new("Ui.TimelineContent", typeof(UiTimelineContent), "flux:timeline.content", "What the item says, beside its indicator.", []),
        new("Ui.TimelineBlock", typeof(UiTimelineBlock), "flux:timeline.block", "A card or a callout spanning the timeline's width, the line running into it and out again.", []),
        new("Ui.TimelineSubgrid", typeof(UiTimelineSubgrid), "flux:timeline.subgrid", "Inside a block: its first child in the indicators' column, its second in the content's.", []),
    ];

    public static IReadOnlyList<UiApiPart> Carousel { get; } =
    [
        new("Ui.Carousel", typeof(UiCarousel), "flux:carousel", "Slides that scroll sideways and come to rest on one. The browser scrolls; the runtime moves the rest.",
        [
            new("Arrows", typeof(bool?), "Whether it has its own previous and next arrows. On unless false."),
            new("ArrowsPosition", typeof(Ui.CarouselArrowsPosition?), "Where those arrows sit. Inside when unset."),
            new("ArrowsClass", typeof(string), "Classes added to each of those arrows."),
            new("Autoplay", typeof(bool?), "Advances by itself, holding while it is hovered, focused or moved by hand, and never for a reader who asked for less motion."),
            new("AutoplayInterval", typeof(int?), "Milliseconds between two advances. 5000 when unset."),
            new("Indicators", typeof(bool?), "One button per slide under the carousel, each going to its slide."),
            new("Disabled", typeof(bool?), "Takes the arrows and the indicators out of use. The slides still scroll."),
            new("Fade", typeof(bool?), "Fades the edge of the track that has more slides behind it."),
            new("Bleed", typeof(bool?), "Runs the track through the padding around it, by --ui-bleed (a card sets it) or 1.5rem."),
            new("Snap", typeof(Ui.CarouselSnap?), "How firmly it rests on a slide. Proximity when unset."),
            new("Scroll", typeof(Ui.CarouselScroll?), "How an arrow or an indicator moves it. Smooth when unset."),
            new("Advance", typeof(Ui.CarouselAdvance?), "How far an arrow moves it: a slide, or the slides in view. Slide when unset."),
            new("Wrap", typeof(Ui.CarouselWrap?), "What advancing past the last slide does. None when unset; Rewind goes back to the first."),
            new("Name", typeof(string), "A name unique on the page, for a Ui.CarouselControls elsewhere to move it by."),
            new("TrackClass", typeof(string), "Classes added to the scrolling track."),
        ]),
        new("Ui.CarouselSlide", typeof(UiCarouselSlide), "flux:carousel.slide", "One slide. Its class is its width, and so how many are in view.", []),
        new("Ui.CarouselControls", typeof(UiCarouselControls), "flux:carousel.controls", "Previous and next buttons anywhere on the page, for the carousel of that name.",
        [
            new("Name", typeof(string), "The Name of the carousel they move."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Flag { get; } =
    [
        new("Ui.Flag", typeof(UiFlag), "flux:flag", "A country's flag: one <img loading=\"lazy\"> of /flags/<CC>.svg, 3:2.",
        [
            new("Country", typeof(string), "The Unicode region code, in either case: \"US\", \"de\". A code with no drawing shows the empty frame."),
            new("Src", typeof(string), "An image of your own (a subdivision, an organization), which wins over Country."),
            new("Alt", typeof(string), "What the flag says, for a reader who cannot see it. Empty when unset: decoration."),
            new("Size", typeof(Ui.FlagSize?), "How wide it is. Sm, 24px, when unset; a w-* in Class wins over it."),
            new("Circle", typeof(bool?), "Crops it to a circle."),
        ]),
    ];

    /// <summary>Every page's parts, for the tests that hold them to the components.</summary>
    public static IEnumerable<UiApiPart> All =>
        new[]
        {
            Accordion, Avatar, Badge, Button, Callout, Card, Carousel, Chart, Command, Context, Dropdown, Flag, Heading, Icon,
            Kanban, Modal, Popover, Progress, Separator, Skeleton, Table, Text, Timeline, Toast, Tooltip,
        }.SelectMany(parts => parts);
}
