namespace Rask.Site.Features.UiDocs;

/// <summary>
///     Every page of the UI tab, in the order its sidebar lists them: the pages that explain Rask.Ui, then one page per
///     component in Flux UI's own order.
/// </summary>
/// <remarks>
///     A component joins this list when its page exists, and not before: the sidebar names no page that is not there.
///     <c>UiDocsCatalogTests</c> holds the component order to Flux's catalogue.
/// </remarks>
internal static class UiDocsCatalog
{
    public const string GettingStarted = "Getting started";

    public const string Layouts = "Layouts";

    public const string Components = "Components";

    public static UiDocsEntry Introduction { get; } = new(
        Routes.UiIntroductionPage(), "Introduction", GettingStarted,
        "Rask.Ui: Flux UI components in C#",
        "Rask.Ui is Flux UI's component catalogue as typed C# components for ASP.NET Core and WebAssembly: one package, "
        + "one stylesheet import, no npm.");

    public static UiDocsEntry Theming { get; } = new(
        Routes.UiThemingPage(), "Theming", GettingStarted,
        "Theming Rask.Ui: base gray, accent and font",
        "Re-skin Rask.Ui from your own Tailwind theme: point the zinc base and the three accent tokens at other "
        + "colours, and keep or replace the Inter font.");

    public static UiDocsEntry DarkMode { get; } = new(
        Routes.UiDarkModePage(), "Dark mode", GettingStarted,
        "Dark mode in Rask.Ui: light, dark or system",
        "Rask.Ui's dark mode is a dark class on html, set before the first paint from the reader's choice or their "
        + "system, and switched with no C# handler.");

    public static UiDocsEntry Header { get; } = new(
        Routes.UiHeaderPage(), "Header", Layouts,
        "Header layout in C#: Ui.Header and Ui.Main",
        "Flux UI's header layout in C#: a full-width bar with the brand, a navbar and the account over the main, and a "
        + "sidebar that takes the links on a phone.",
        "header");

    public static UiDocsEntry Sidebar { get; } = new(
        Routes.UiSidebarPage(), "Sidebar", Layouts,
        "Sidebar layout in C#: Ui.Sidebar and Ui.Main",
        "Flux UI's sidebar layout in C#: navigation down the side that slides over the page on a phone and narrows to a "
        + "rail of icons, with no script.",
        "sidebar");

    public static UiDocsEntry Accordion { get; } = new(
        Routes.UiAccordionPage(), "Accordion", Components,
        "Accordion component in C#: Ui.Accordion",
        "Flux UI's accordion in C#: items on native details that open with no handler, one at a time when "
        + "exclusive, animated, and opened by find-in-page.",
        "accordion");

    public static UiDocsEntry Avatar { get; } = new(
        Routes.UiAvatarPage(), "Avatar", Components,
        "Avatar component in C#: Ui.Avatar",
        "Flux UI's avatar in C#: a picture, or initials or an icon when there is none, in five sizes and "
        + "seventeen hues, with a badge, and stacked in groups.",
        "avatar");

    public static UiDocsEntry Badge { get; } = new(
        Routes.UiBadgePage(), "Badge", Components,
        "Badge component in C#: Ui.Badge",
        "Flux UI's badge as a C# component: a status, category or count in any Tailwind colour and three sizes, "
        + "pressable, removable and set into a line of text.",
        "badge");

    public static UiDocsEntry Brand { get; } = new(
        Routes.UiBrandPage(), "Brand", Components,
        "Brand component in C#: Ui.Brand",
        "Flux UI's brand in C#: the product's mark and name linking home, at the head of a header or a sidebar, from "
        + "an image or a mark you draw.",
        "brand");

    public static UiDocsEntry Breadcrumbs { get; } = new(
        Routes.UiBreadcrumbsPage(), "Breadcrumbs", Components,
        "Breadcrumbs in C#: Ui.Breadcrumbs",
        "Flux UI's breadcrumbs in C#: the trail to the page being shown, with chevrons or slashes, icons for "
        + "words, and the folded steps in a dropdown.",
        "breadcrumbs");

    public static UiDocsEntry Navbar { get; } = new(
        Routes.UiNavbarPage(), "Navbar", Components,
        "Navbar component in C#: Ui.Navbar",
        "Flux UI's navbar in C#: a header's links in a row, the current one underlined in the accent, with icons, "
        + "badges and an item that opens a menu.",
        "navbar");

    public static UiDocsEntry Navlist { get; } = new(
        Routes.UiNavlistPage(), "Navlist", Components,
        "Navlist component in C#: Ui.Navlist",
        "Flux UI's navlist in C#: a sidebar's links in a column, with icons, badges, and groups that fold away "
        + "with no script, the current page found from the route.",
        "navbar");

    public static UiDocsEntry Pagination { get; } = new(
        Routes.UiPaginationPage(), "Pagination", Components,
        "Pagination component in C#: Ui.Pagination",
        "Flux UI's pagination in C#: a summary, Previous and Next and the pages numbered where there is room, as "
        + "buttons or links that keep the page in the URL.",
        "pagination");

    public static UiDocsEntry Profile { get; } = new(
        Routes.UiProfilePage(), "Profile", Components,
        "Profile button in C#: Ui.Profile",
        "Flux UI's profile in C#: the signed-in person as a button, an avatar or initials, a name and a chevron, "
        + "that opens the account menu.",
        "profile");

    public static UiDocsEntry Tabs { get; } = new(
        Routes.UiTabsPage(), "Tabs", Components,
        "Tabs component in C#: Ui.Tabs",
        "Flux UI's tabs in C#: a row, segmented control or pills over panels, switched in the browser, with the "
        + "selected tab a value the page can bind.",
        "tabs");

    public static UiDocsEntry Button { get; } = new(
        Routes.UiButtonPage(), "Button", Components,
        "Button and button group in C#: Ui.Button",
        "Flux UI's button in C#: six variants, three sizes, any Tailwind colour, icons, a spinner while its handler "
        + "runs, links that stay in the app, and button groups.",
        "button");

    public static UiDocsEntry Callout { get; } = new(
        Routes.UiCalloutPage(), "Callout", Components,
        "Callout component in C#: Ui.Callout",
        "Flux UI's callout in C#: an icon, a heading, text and actions to draw attention to a message, in four "
        + "variants or any colour, inline or dismissible.",
        "callout");

    public static UiDocsEntry Card { get; } = new(
        Routes.UiCardPage(), "Card", Components,
        "Card component in C#: Ui.Card",
        "Flux UI's card in C#: a header, body and footer with the spacing, dividers and corners between them, in "
        + "five surfaces, four sizes and six body treatments.",
        "card");

    public static UiDocsEntry Carousel { get; } = new(
        Routes.UiCarouselPage(), "Carousel", Components,
        "Carousel component in C#: Ui.Carousel",
        "Flux UI's carousel in C#: slides that scroll and snap in the browser, with arrows, indicators, "
        + "autoplay and controls elsewhere, and nothing sent to the server.",
        "carousel");

    public static UiDocsEntry Chart { get; } = new(
        Routes.UiChartPage(), "Chart", Components,
        "Charts in C#: Ui.Chart",
        "Flux UI's chart in C#: line, area, bar and pie charts drawn as SVG in the render, with axes, a "
        + "tooltip, a cursor, a live summary and a legend.",
        "chart");

    public static UiDocsEntry Command { get; } = new(
        Routes.UiCommandPage(), "Command palette", Components,
        "Command palette in C#: Ui.Command",
        "Flux UI's command palette in C#: a searchable list of commands that narrows as you type, walks with the "
        + "arrows, and opens as a modal from a shortcut.",
        "command");

    public static UiDocsEntry Context { get; } = new(
        Routes.UiContextPage(), "Context menu", Components,
        "Context menu in C#: Ui.Context",
        "Flux UI's context menu in C#: the menu a dropdown opens, at the pointer on a right-click, with the same "
        + "rows, submenus and keyboard.",
        "context");

    public static UiDocsEntry Dropdown { get; } = new(
        Routes.UiDropdownPage(), "Dropdown", Components,
        "Dropdown menu in C#: Ui.Dropdown and Ui.Menu",
        "Flux UI's dropdown in C#: a menu of actions or links under a button, with submenus, checkbox and radio "
        + "rows, groups, shortcuts and Flux's keyboard.",
        "dropdown");

    public static UiDocsEntry Flag { get; } = new(
        Routes.UiFlagPage(), "Flag", Components,
        "Country flags in C#: Ui.Flag",
        "Flux UI's flag in C#: a country's flag from its two-letter region code, 3:2 or round, in five "
        + "widths, each a small file the page fetches when it shows it.",
        "flag");

    public static UiDocsEntry Heading { get; } = new(
        Routes.UiHeadingPage(), "Heading", Components,
        "Heading component in C#: Ui.Heading",
        "Flux UI's heading in C#: four sizes, an outline level kept apart from the size, and the line of text that "
        + "sits under a heading.",
        "heading");

    public static UiDocsEntry Icon { get; } = new(
        Routes.UiIconPage(), "Icon", Components,
        "Heroicons in C#: Ui.Icon",
        "Every Heroicon as a C# component: Ui.Icon in outline, solid, mini and micro, sized and coloured with a "
        + "Tailwind class, and taken by every Rask.Ui component.",
        "icon");

    public static UiDocsEntry Filters { get; } = new(
        Routes.UiFiltersPage(), "Filters", Components,
        "List filters in C#: Ui.Filters",
        "List filters kept in the page's address: a Filters button opening a flyout of fields that apply as chosen, "
        + "and a badge for each filter in force.");

    public static UiDocsEntry Kanban { get; } = new(
        Routes.UiKanbanPage(), "Kanban", Components,
        "Kanban board in C#: Ui.Kanban",
        "Flux UI's kanban in C#: cards in columns, one per stage, with counts, actions and a footer, and "
        + "cards that open as buttons. The page owns the lists.",
        "kanban");

    public static UiDocsEntry Modal { get; } = new(
        Routes.UiModalPage(), "Modal", Components,
        "Modal dialog in C#: Ui.Modal",
        "Flux UI's modal in C#: a real dialog its trigger opens with no handler, as a confirmation, a flyout from "
        + "any edge, or opened by a C# field.",
        "modal");

    public static UiDocsEntry Popover { get; } = new(
        Routes.UiPopoverPage(), "Popover", Components,
        "Popover component in C#: Ui.Popover",
        "Flux UI's popover in C#: a panel of extra content beside a button (filters, a form, a profile card) "
        + "opened by a press or by the pointer.",
        "popover");

    public static UiDocsEntry Progress { get; } = new(
        Routes.UiProgressPage(), "Progress", Components,
        "Progress bar in C#: Ui.Progress",
        "Flux UI's progress bar in C#: a value out of any maximum, in the accent or any Tailwind colour, announced "
        + "as a progressbar and moving to each new value.",
        "progress");

    public static UiDocsEntry Skeleton { get; } = new(
        Routes.UiSkeletonPage(), "Skeleton", Components,
        "Skeleton loader in C#: Ui.Skeleton",
        "Flux UI's skeleton in C#: blocks and lines in the shape of what is loading, so the layout does not jump, "
        + "still or animated together with a shimmer or a pulse.",
        "skeleton");

    public static UiDocsEntry Timeline { get; } = new(
        Routes.UiTimelinePage(), "Timeline", Components,
        "Timeline component in C#: Ui.Timeline",
        "Flux UI's timeline in C#: events or steps in order, down the page or across it, with status, "
        + "coloured or bare indicators, blocks and spacing.",
        "timeline");

    public static UiDocsEntry Toast { get; } = new(
        Routes.UiToastPage(), "Toast", Components,
        "Toast notifications in C#: Ui.Toast",
        "Flux UI's toast in C#: raised from any handler with Toast.Success(\"Saved\"), with a heading, an action or "
        + "a link, in any corner, one at a time or stacked.",
        "toast");

    public static UiDocsEntry Tooltip { get; } = new(
        Routes.UiTooltipPage(), "Tooltip", Components,
        "Tooltip component in C#: Ui.Tooltip",
        "Flux UI's tooltip in C#: a line of help on hover and keyboard focus that names an icon button, on any side, "
        + "or opened by a tap where it must reach a phone.",
        "tooltip");

    public static UiDocsEntry Separator { get; } = new(
        Routes.UiSeparatorPage(), "Separator", Components,
        "Separator component in C#: Ui.Separator",
        "Flux UI's separator in C#: a line between sections or between items in a row, across or down, subtle, "
        + "or with a word such as \"or\" in the middle.",
        "separator");

    public static UiDocsEntry Table { get; } = new(
        Routes.UiTablePage(), "Table", Components,
        "Data table in C#: Ui.Table",
        "Flux UI's table in C#: sortable headings, a pager, sticky headings and columns, rows that dim "
        + "while a slow page loads, and widths that hold from page to page.",
        "table");

    public static UiDocsEntry Text { get; } = new(
        Routes.UiTextPage(), "Text & link", Components,
        "Text and link components in C#: Ui.Text, Ui.Link",
        "Flux UI's text and link in C#: body copy in three inks, four sizes or any Tailwind hue, and a link that "
        + "stays in the app for a route or leaves it for a URL.",
        "text");

    public static UiDocsEntry Checkbox { get; } = new(
        Routes.UiCheckboxPage(), "Checkbox", Components,
        "Checkbox and checkbox group in C#: Ui.Checkbox",
        "Flux UI's checkbox in C#: one bound to a bool, a group bound to a collection with a check-all, drawn as "
        + "cards, pills or buttons.",
        "checkbox");

    public static UiDocsEntry Field { get; } = new(
        Routes.UiFieldPage(), "Field", Components,
        "Form field in C#: Ui.Field, Ui.Label, Ui.Error",
        "Flux UI's field in C#: a label, a control, help text and the message the form holds for it, with badges, "
        + "split rows and fieldsets.",
        "field");

    public static UiDocsEntry Input { get; } = new(
        Routes.UiInputPage(), "Input", Components,
        "Text input in C#: Ui.Input and Ui.InputGroup",
        "Flux UI's input in C#: every input type, icons, a keyboard hint, clear, copy and reveal buttons, masks, "
        + "and prefixes and buttons joined to it.",
        "input");

    public static UiDocsEntry Otp { get; } = new(
        Routes.UiOtpPage(), "OTP input", Components,
        "One-time code input in C#: Ui.Otp",
        "Flux UI's OTP input in C#: a cell per character bound as one string, pasted codes, letters or digits, "
        + "private cells, groups and separators.",
        "otp-input");

    public static UiDocsEntry Radio { get; } = new(
        Routes.UiRadioPage(), "Radio", Components,
        "Radio group in C#: Ui.RadioGroup and Ui.Radio",
        "Flux UI's radio in C#: a group bound to one value of any type, drawn as a list, a segmented control, "
        + "cards, pills or buttons.",
        "radio");

    public static UiDocsEntry Select { get; } = new(
        Routes.UiSelectPage(), "Select", Components,
        "Select, listbox and combobox in C#: Ui.Select",
        "Flux UI's select in C#: the browser's own, a listbox with icons and avatars, search, several picks, a "
        + "combobox and options found on the server.",
        "select");

    public static UiDocsEntry Slider { get; } = new(
        Routes.UiSliderPage(), "Slider", Components,
        "Slider and range slider in C#: Ui.Slider",
        "Flux UI's slider in C#: a value or a range between two thumbs, steps, big steps on Shift, and ticks, dots "
        + "or labels under the track.",
        "slider");

    public static UiDocsEntry Switch { get; } = new(
        Routes.UiSwitchPage(), "Switch", Components,
        "Toggle switch in C#: Ui.Switch",
        "Flux UI's switch in C#: a setting turned on or off at once, bound to a bool, with a label and a "
        + "description, after its words or before them.",
        "switch");

    public static UiDocsEntry Textarea { get; } = new(
        Routes.UiTextareaPage(), "Textarea", Components,
        "Textarea in C#: Ui.Textarea",
        "Flux UI's textarea in C#: several lines of text with a label, a set number of rows or a height that grows "
        + "with what is typed, and a resize handle.",
        "textarea");

    public static UiDocsEntry Autocomplete { get; } = new(
        Routes.UiAutocompletePage(), "Autocomplete", Components,
        "Autocomplete input in C#: Ui.Autocomplete",
        "Flux UI's autocomplete in C#: an input that suggests what to type, writes the picked text into itself, and asks "
        + "the server once typing has paused.",
        "autocomplete");

    public static UiDocsEntry Calendar { get; } = new(
        Routes.UiCalendarPage(), "Calendar", Components,
        "Calendar component in C#: Ui.Calendar",
        "Flux UI's calendar in C#: a day, several days or a range from a month grid, with limits, unavailable days, week "
        + "numbers, inputs and the keyboard.",
        "calendar");

    public static UiDocsEntry ColorPicker { get; } = new(
        Routes.UiColorPickerPage(), "Color picker", Components,
        "Color picker in C#: Ui.ColorPicker",
        "Flux UI's color picker in C#: swatches, a saturation area, hue and alpha sliders, hex, rgb or hsl typed in, a "
        + "button trigger, a dropper and copy.",
        "color-picker");

    public static UiDocsEntry Composer { get; } = new(
        Routes.UiComposerPage(), "Composer", Components,
        "Chat message composer in C#: Ui.Composer",
        "Flux UI's composer in C#: a message box with buttons around it for a chat or a prompt, sent on Cmd+Enter or "
        + "Enter, growing a line at a time.",
        "composer");

    public static UiDocsEntry DatePicker { get; } = new(
        Routes.UiDatePickerPage(), "Date picker", Components,
        "Date picker and range picker in C#: Ui.DatePicker",
        "Flux UI's date picker in C#: a day or a range from a calendar popup or typed in parts, with presets, limits, a "
        + "clear button and a sheet on a phone.",
        "date-picker");

    public static UiDocsEntry DatetimePicker { get; } = new(
        Routes.UiDatetimePickerPage(), "Datetime picker", Components,
        "Date and time picker in C#: Ui.DatetimePicker",
        "Flux UI's datetime picker in C#: a date and a time, or a range, from one field: a calendar beside a list of "
        + "times, typed times, limits and unavailable slots.",
        "datetime-picker");

    public static UiDocsEntry Editor { get; } = new(
        Routes.UiEditorPage(), "Rich text editor", Components,
        "Rich text editor in C#: Ui.Editor",
        "Flux UI's rich text editor in C#: Tiptap under a toolbar you name or compose, bound to HTML, with Markdown "
        + "shortcuts and buttons of your own.",
        "editor");

    public static UiDocsEntry FileUpload { get; } = new(
        Routes.UiFileUploadPage(), "File upload", Components,
        "File upload with drag and drop in C#: Ui.FileUpload",
        "Flux UI's file upload in C#: a dropzone that takes files clicked, dropped or reached by keyboard, file items, "
        + "progress, and an uploader of your own.",
        "file-upload");

    public static UiDocsEntry Phone { get; } = new(
        Routes.UiPhonePage(), "Phone", Components,
        "Phone number input in C#: Ui.Phone",
        "Flux UI's phone field in C#: an international number with a searchable list of flags, formatted as it is typed "
        + "and bound as E.164.",
        "phone");

    public static UiDocsEntry Pillbox { get; } = new(
        Routes.UiPillboxPage(), "Pillbox", Components,
        "Pillbox multi-select in C#: Ui.Pillbox",
        "Flux UI's pillbox in C#: several answers shown as pills to take off again, with search, an input among the "
        + "pills, a create row and options from the server.",
        "pillbox");

    public static UiDocsEntry TimePicker { get; } = new(
        Routes.UiTimePickerPage(), "Time picker", Components,
        "Time picker in C#: Ui.TimePicker",
        "Flux UI's time picker in C#: one time or several from a list or typed in parts, 12- or 24-hour, with intervals, "
        + "limits and unavailable times.",
        "time-picker");

    public static UiDocsEntry Toggle { get; } = new(
        Routes.UiTogglePage(), "Toggle", Components,
        "Toggle button in C#: Ui.Toggle",
        "Flux UI's toggle in C#: a setting turned on or off from a compact button whose icon fills in, with a tooltip, a "
        + "label and a spinner while it saves.",
        "toggle");

    /// <summary>Every page, in sidebar order.</summary>
    public static IReadOnlyList<UiDocsEntry> All { get; } =
    [
        Introduction, Theming, DarkMode,
        Header, Sidebar,
        Accordion, Autocomplete, Avatar, Badge, Brand, Button, Breadcrumbs, Calendar, Callout, Card, Carousel, Chart, Checkbox,
        ColorPicker, Command, Context, Composer, DatePicker, DatetimePicker, Dropdown, Editor, Field, FileUpload, Filters, Flag, Heading, Icon,
        Input, Kanban, Modal, Navbar, Navlist, Otp, Pagination, Phone, Pillbox, Popover, Profile, Progress, Radio, Select,
        Separator, Skeleton, Slider, Switch, Table, Tabs, Text, Textarea, Timeline, TimePicker, Toast, Toggle, Tooltip,
    ];
}
