using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

// The layouts' tables: the header and the sidebar. Both pages list the toggle and the main, as Flux's do.
internal static partial class UiApi
{
    // Built once, so both pages hold the same part and the reference tests count it once. Above the tables in the
    // same file, so they are set before them: initialisers run in their file's order.
    private static readonly UiApiPart SidebarTogglePart =
        new("Ui.SidebarToggle", typeof(UiSidebarToggle), "flux:sidebar.toggle",
            "The button that slides the sidebar over the page: the hamburger in a phone's header. Not shown from the sidebar's breakpoint up.",
        [
            new("Icon", typeof(Ui.IconName?), "The icon drawn. Bars2 when unset."),
            new("Inset", typeof(Ui.Position?), "Pulls the button into its container's padding on that side: Flux's inset=\"left\"."),
        ]);

    private static readonly UiApiPart MainPart =
        new("Ui.Main", typeof(UiMain), "flux:main", "The page's content, beside the sidebar and under the header. A <div>: put a <main> inside it.",
        [
            new("Inset", typeof(bool?), "A bordered, rounded panel with a gutter round it; from lg up the layout fills the viewport and the panel scrolls."),
            new("Container", typeof(bool?), "Holds the content to the container width, centred: inside the panel, when inset."),
        ]);

    public static IReadOnlyList<UiApiPart> Header { get; } =
    [
        new("Ui.Header", typeof(UiHeader), "flux:header", "The bar across the top: a brand, the navigation, the account. It paints no ground of its own.",
        [
            new("Sticky", typeof(bool?), "Keeps the header at the top of the viewport while the page scrolls under it."),
            new("Container", typeof(bool?), "Holds what is in it to the container width, centred, while its ground runs edge to edge."),
        ]),
        SidebarTogglePart,
        MainPart,
    ];

    public static IReadOnlyList<UiApiPart> Sidebar { get; } =
    [
        new("Ui.Sidebar", typeof(UiSidebar), "flux:sidebar", "The application's navigation, beside the page. It paints no ground of its own.",
        [
            new("Sticky", typeof(bool?), "Keeps the docked sidebar in view while the page scrolls, scrolling inside itself when it is taller."),
            new("Collapsible", typeof(Ui.SidebarCollapsible?), "When it can be put away: slid over a phone (Mobile), and narrowed to a rail too (Always). Never when unset."),
            new("Breakpoint", typeof(Ui.Breakpoint?), "The width it docks from. Lg, 1024px, when unset."),
            new("Persist", typeof(bool?), "Whether the rail is remembered across visits. On unless false."),
            new("Stashable", typeof(bool?), "Flux's deprecated spelling of Collapsible(Ui.SidebarCollapsible.Mobile)."),
        ]),
        new("Ui.SidebarHeader", typeof(UiSidebarHeader), "flux:sidebar.header", "The top of the sidebar, for its brand and its collapse control.", []),
        new("Ui.SidebarBrand", typeof(UiSidebarBrand), "flux:sidebar.brand", "The logo and the name, linking home. Narrowed to the rail it is the logo alone.",
        [
            new("Href", typeof(RouteUrl?), "Where it leads. A generated route navigates inside the app.", "a route, or a URL string"),
            new("Logo", typeof(string), "The logo image's address."),
            new("LogoDark", typeof(string), "The logo shown in dark mode instead: Flux's logo:dark."),
            new("Name", typeof(string), "The name beside the logo."),
        ]),
        new("Ui.SidebarCollapse", typeof(UiSidebarCollapse), "flux:sidebar.collapse", "Narrows a docked sidebar to its rail, and closes one slid over the page.",
        [
            new("Inset", typeof(Ui.Position?), "Pulls the control into its container's padding on that side."),
            new("Tooltip", typeof(string), "What the control is called. \"Toggle sidebar\" when unset."),
        ]),
        new("Ui.SidebarSearch", typeof(UiSidebarSearch), "flux:sidebar.search", "A button that looks like a field, the way into a search; a real field once given OnInput.",
        [
            new("Placeholder", typeof(string), "The words shown while nothing has been typed, and its tooltip in the rail."),
            new("Value", typeof(string), "What the reader has typed, for a search that is a field."),
            new("OnInput", typeof(Callback<string>), "Runs on every keystroke with the text so far, and makes the search a field.", "a handler given the text: text => …"),
        ]),
        new("Ui.SidebarNav", typeof(UiSidebarNav), "flux:sidebar.nav", "A list of items and groups: a navigation landmark.", []),
        new("Ui.SidebarItem", typeof(UiSidebarItem), "flux:sidebar.item", "One destination. Its children are its label; in the rail it is its icon and a tooltip.",
        [
            new("Href", typeof(RouteUrl?), "Where it leads. A generated route navigates inside the app; a string is an ordinary link.", "a route, or a URL string"),
            new("Icon", typeof(Ui.IconName?), "The icon before the label, and all that is left of the item in the rail."),
            new("Badge", typeof(string), "A short value at the item's trailing edge: a count, a state."),
            new("Current", typeof(bool?), "Whether this is the page being shown. Unset, the router decides."),
            new("Tooltip", typeof(string), "What the tooltip says while the rail hides the label. The label when unset."),
        ]),
        new("Ui.SidebarGroup", typeof(UiSidebarGroup), "flux:sidebar.group", "Items under a heading, which can fold away; in the rail, its icon and a menu.",
        [
            new("Heading", typeof(string), "The words above the group's items."),
            new("Expandable", typeof(bool?), "Lets the reader fold the group away and open it again: a native <details>."),
            new("Icon", typeof(Ui.IconName?), "The icon before the heading, in place of the chevron, and what the group is in the rail."),
            new("Expanded", typeof(bool?), "Whether an expandable group is open. Open unless false."),
            new("OnToggle", typeof(Callback<bool>), "Runs when the reader opens or folds the group, with the state asked for.", Toggled),
        ]),
        new("Ui.SidebarSpacer", typeof(UiSidebarSpacer), "flux:sidebar.spacer", "Takes the leftover height, pushing what follows to the bottom.", []),
        new("Ui.SidebarProfile", typeof(UiSidebarProfile), "flux:sidebar.profile", "Who is signed in, at the foot: the trigger of the account menu a dropdown puts around it.",
        [
            new("Avatar", typeof(string), "The picture's address. Without one the name's initials stand in."),
            new("Name", typeof(string), "The person's name."),
        ]),
        SidebarTogglePart,
        MainPart,
    ];
}
