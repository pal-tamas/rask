using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Rask.Server.E2E.Tests.Infrastructure.KitGestures;

namespace Rask.Server.E2E.Tests;

public abstract partial class KitDrawsWithoutAskingTests
{
    /// <summary>A component, the page it is on and what a reader does to it there.</summary>
    internal sealed record Family(
        Type Page, IReadOnlyList<string> Gestures, Func<PlaywrightFixture, Task<IReadOnlyList<KitCount>>> Count);

    private const string NextMonth = " [aria-label='Next month']";
    private const string PreviousMonth = " [aria-label='Previous month']";
    private const string ADay = " [role=gridcell] button >> nth=20";
    private const string ColorButton = "[data-c='color-picker'] [data-ui-color-picker-swatch-button]";
    private const string ColorArea = "[data-c='color-picker'] dialog [data-rask-drag]";
    private const string CommandButton = "[data-c='command'] > button";
    private const string RangeStart = "[data-c='calendar-range'] [role=gridcell] button >> nth=10";
    private const string Rail = "label[for=sidebar-rail][role=button]";
    private const string PhoneNumber = "[data-c='phone'] input[type=tel]";
    private const string PhoneButton = "[data-c='phone'] button[role=combobox]";

    // The phone engine is a file the field fetches: every gesture waits for it to have taken the field over.
    private static readonly Func<Microsoft.Playwright.IPage, Task> PhoneMounted = page =>
        page.Locator("[data-c='phone'] [data-ui-phone-ready]").WaitForAsync(new() { State = Microsoft.Playwright.WaitForSelectorState.Attached });

    private static readonly Func<Microsoft.Playwright.IPage, Task> PhoneOpen = async page =>
    {
        await PhoneMounted(page);
        await page.Locator(PhoneButton).ClickAsync();
    };

    private static readonly Dictionary<string, Family> Families = new(StringComparer.Ordinal)
    {
        ["accordion"] = On<KitDisplayFramePage>("accordion", Twice("open and close an item", "[data-c='accordion'] summary")),
        ["accordion-exclusive"] = On<KitDisplayFramePage>("accordion-exclusive",
            Twice("open one item, then another", "[data-c='accordion-exclusive'] summary", "[data-c='accordion-exclusive'] details:nth-of-type(2) summary")),
        ["avatar"] = On<KitDisplayFramePage>("avatar"),
        ["badge"] = On<KitDisplayFramePage>("badge"),
        ["breadcrumbs"] = On<KitDisplayFramePage>("breadcrumbs"),
        ["button"] = On<KitDisplayFramePage>("button"),
        ["callout"] = On<KitDisplayFramePage>("callout"),
        ["card"] = On<KitDisplayFramePage>("card"),
        ["kanban"] = On<KitDisplayFramePage>("kanban"),
        ["pagination"] = On<KitDisplayFramePage>("pagination", Press("go to another page", "[data-c='pagination'] button[data-rask-key='3']")),
        ["progress"] = On<KitDisplayFramePage>("progress"),
        ["separator"] = On<KitDisplayFramePage>("separator"),
        ["skeleton"] = On<KitDisplayFramePage>("skeleton"),
        ["table"] = On<KitDisplayFramePage>("table", Press("press a sortable column the page gave no handler", "[data-c='table'] [data-ui-table-sortable]")),
        ["timeline"] = On<KitDisplayFramePage>("timeline"),
        ["tooltip"] = On<KitDisplayFramePage>("tooltip", Twice("open and close a toggleable tooltip", "[data-c='tooltip'] [popovertarget]")),
        ["profile"] = On<KitDisplayFramePage>("profile"),

        ["checkbox"] = On<KitFieldFramePage>("checkbox", Press("tick", "[data-c='checkbox'] [data-ui-checkbox]")),
        ["checkbox-group"] = On<KitFieldFramePage>("checkbox-group", Press("tick one", "[data-c='checkbox-group'] [data-ui-checkbox] >> nth=1")),
        ["radio"] = On<KitFieldFramePage>("radio",
            Press("choose", "[data-c='radio'] [data-ui-radio] >> nth=1"),
            Keys("choose by the arrows", Focusing("[data-c='radio'] input"), "ArrowDown", "ArrowDown")),
        ["radio-segmented"] = On<KitFieldFramePage>("radio-segmented", Press("choose", "[data-c='radio-segmented'] label >> nth=1")),
        ["switch"] = On<KitFieldFramePage>("switch", Press("flip", "[data-c='switch'] [data-ui-switch]")),
        ["input"] = On<KitFieldFramePage>("input", Typing("[data-c='input'] input", "abc"), TypingThenLeaving("[data-c='input'] input", "abc")),
        ["input-clearable"] = On<KitFieldFramePage>("input-clearable",
            Press("clear", "[data-c='input-clearable'] [data-rask-clear]", Hovering("[data-c='input-clearable'] input"))),
        ["input-copyable"] = On<KitFieldFramePage>("input-copyable", Press("copy", "[data-c='input-copyable'] [data-rask-copy]")),
        ["input-viewable"] = On<KitFieldFramePage>("input-viewable", Twice("show and hide the password", "[data-c='input-viewable'] button")),
        ["input-mask"] = On<KitFieldFramePage>("input-mask", Typing("[data-c='input-mask'] input", "5551234567")),
        ["textarea"] = On<KitFieldFramePage>("textarea",
            Typing("[data-c='textarea'] textarea", "abc"), TypingThenLeaving("[data-c='textarea'] textarea", "abc")),
        ["composer"] = On<KitFieldFramePage>("composer",
            Typing("[data-c='composer'] textarea", "abc"),
            TypingThenLeaving("[data-c='composer'] textarea", "abc"),
            Keys("new lines that grow the box", Focusing("[data-c='composer'] textarea"), "Enter", "Enter", "Enter"),
            Keys("send by Ctrl+Enter", Focusing("[data-c='composer'] textarea"), "a", "Control+Enter")),
        ["field"] = On<KitFieldFramePage>("field"),
        ["slider"] = On<KitFieldFramePage>("slider",
            DragBy("[data-c='slider'] [data-ui-slider-thumb]", 120, release: false),
            DragBy("[data-c='slider'] [data-ui-slider-thumb]", 120, release: true),
            Keys("arrow keys", Focusing("[data-c='slider'] input"), "ArrowRight", "ArrowRight", "ArrowLeft", "End", "Home")),
        ["otp"] = On<KitFieldFramePage>("otp",
            Typing("[data-c='otp'] [data-ui-otp-input]", "12", "typing half the code"),
            Typing("[data-c='otp'] [data-ui-otp-input]", "1234", "typing the whole code")),
        ["file-upload"] = On<KitFieldFramePage>("file-upload"),
        ["phone"] = On<KitFieldFramePage>("phone",
        [
            Typing(PhoneNumber, "2015550123", arrange: PhoneMounted),
            new("typing, then leaving the field", async page =>
            {
                await page.Locator(PhoneNumber).ClickAsync();
                await page.Keyboard.TypeAsync("2015550123");
                await page.Keyboard.PressAsync("Tab");
            }, PhoneMounted),
            Press("open by a press", PhoneButton, PhoneMounted),
            Pointer("[data-c='phone'] [popover]", PhoneOpen, "pointer over the open popup"),
            Cursor(PhoneOpen),
            Typing("[data-c='phone'] [popover] input", "hun", "typing to filter", PhoneOpen),
            Escape(PhoneOpen),
            Outside(PhoneOpen),
            Press("pick a country", "[data-c='phone'] [role=option] >> nth=3", PhoneOpen),
        ]),

        ["dropdown"] = On<KitOverlayFramePage>("dropdown", Popup("#dropdown-open", "[data-c='dropdown'] [popover]")),
        ["dropdown-hover"] = On<KitOverlayFramePage>("dropdown-hover"),
        ["dropdown-heard"] = On<KitOverlayFramePage>("dropdown-heard", Popup("#heard-open", "[data-c='dropdown-heard'] [popover]")),
        ["context"] = On<KitOverlayFramePage>("context",
            RightPress("open by a right press", "#context-area"),
            Pointer("[data-c='context'] [popover]", RightPressing("#context-area"), "pointer over the open popup"),
            Cursor(RightPressing("#context-area")),
            Escape(RightPressing("#context-area")),
            Outside(RightPressing("#context-area"))),
        ["popover"] = On<KitOverlayFramePage>("popover", Popup("#popover-open", "[data-c='popover'] [popover]")),
        ["popover-hover"] = On<KitOverlayFramePage>("popover-hover"),
        ["navmenu"] = On<KitOverlayFramePage>("navmenu", Popup("#navmenu-open", "[data-c='navmenu'] [popover]")),
        ["modal"] = On<KitOverlayFramePage>("modal", Dialog("modal", "#modal-open")),
        ["modal-flyout"] = On<KitOverlayFramePage>("modal-flyout", Dialog("modal-flyout", "#flyout-open")),
        ["modal-state"] = On<KitOverlayFramePage>("modal-state",
            Press("open by the page's own handler", "#state-open"),
            Escape(Pressing("#state-open")),
            Press("close by its button", "#state-cancel", Pressing("#state-open"))),
        ["command"] = On<KitOverlayFramePage>("command",
            Press("open by a press", CommandButton),
            Typing("[data-c='command'] input", "inv", "typing to filter", Pressing(CommandButton)),
            Cursor(Pressing(CommandButton)),
            Pointer("[data-c='command'] dialog", Pressing(CommandButton), "pointer over the open popup"),
            Escape(Pressing(CommandButton))),
        ["toast"] = On<KitOverlayFramePage>("toast",
            Press("raise one from the page's handler", "#toast-raise"),
            Pointer("[data-ui-toast-dialog]", Pressing("#toast-raise"), "pointer over the shown toast"),
            Press("dismiss it by its button", "[data-rask-dismiss]", Pressing("#toast-raise"))),

        ["tabs"] = On<KitNavigationFramePage>("tabs",
            Press("switch by a press", "[data-c='tabs'] [role=tab] >> nth=1"),
            Keys("switch by the arrows", Focusing("[data-c='tabs'] [role=tab]"), "ArrowRight", "ArrowRight", "ArrowLeft")),
        ["tabs-segmented"] = On<KitNavigationFramePage>("tabs-segmented",
            Press("switch by a press", "[data-c='tabs-segmented'] [role=tab] >> nth=1"),
            Keys("switch by the arrows", Focusing("[data-c='tabs-segmented'] [role=tab]"), "ArrowRight", "ArrowRight", "ArrowLeft")),
        ["navbar"] = On<KitNavigationFramePage>("navbar"),
        ["navlist"] = On<KitNavigationFramePage>("navlist", Twice("fold and unfold a group", "[data-c='navlist'] summary")),
        ["header"] = On<KitNavigationFramePage>("header"),
        ["sidebar"] = Whole<KitSidebarFramePage>(
            Twice("collapse to the rail and back", Rail),
            Twice("fold and unfold a group", "[data-ui-sidebar-group] summary"),
            Pointer(Stage, Pressing(Rail), "pointer over the rail, its tooltips and its menus")),

        ["calendar"] = On<KitPickerFramePage>("calendar",
            Twice("page the month", "[data-c='calendar']" + NextMonth, "[data-c='calendar']" + PreviousMonth),
            Cursor(Focusing("[data-c='calendar'] [data-rask-focus-target]")),
            Press("pick a day", "[data-c='calendar']" + ADay)),
        ["calendar-range"] = On<KitPickerFramePage>("calendar-range",
            Press("pick the first day of a range", RangeStart),
            Pointer("[data-c='calendar-range'] [role=grid]", Pressing(RangeStart), "pointer over the days while a range is being chosen"),
            Press("pick the last day of a range", "[data-c='calendar-range'] [role=gridcell] button >> nth=14", Pressing(RangeStart))),
        ["date-picker"] = On<KitPickerFramePage>("date-picker",
        [
            .. Popup("#arrival", "[data-c='date-picker'] dialog"),
            Twice("page the month", "[data-c='date-picker']" + NextMonth, "[data-c='date-picker']" + PreviousMonth, Pressing("#arrival")),
            Press("pick a day", "[data-c='date-picker']" + ADay, Pressing("#arrival")),
        ]),
        ["date-picker-input"] = On<KitPickerFramePage>("date-picker-input",
            Typing("[data-c='date-picker-input'] [data-rask-segment=month]", "0220", "typing a part of the date"),
            Typing("[data-c='date-picker-input'] [data-rask-segment=month]", "02202027", "typing a whole date")),
        ["time-picker"] = On<KitPickerFramePage>("time-picker",
        [
            .. Popup("#time", "[data-c='time-picker'] [popover]"),
            Press("pick a time", "[data-c='time-picker'] [role=option] >> nth=20", Pressing("#time")),
        ]),
        ["time-picker-input"] = On<KitPickerFramePage>("time-picker-input",
            Typing("[data-c='time-picker-input'] [data-rask-segment=hour]", "10", "typing a part of the time"),
            Typing("[data-c='time-picker-input'] [data-rask-segment=hour]", "1045a", "typing a whole time")),
        ["color-picker"] = On<KitPickerFramePage>("color-picker",
        [
            .. Popup(ColorButton, "[data-c='color-picker'] dialog"),
            Drag(ColorArea, release: false, Pressing(ColorButton)),
            Drag(ColorArea, release: true, Pressing(ColorButton)),
            Typing("[data-c='color-picker'] [data-ui-color-picker-trigger] input", "#12", "typing part of a colour"),
        ]),

        ["select-native"] = On<KitSelectFramePage>("select-native"),
        ["select"] = On<KitSelectFramePage>("select",
        [
            .. Popup("#listbox", "[data-c='select'] [popover]"),
            Keys("type ahead", Pressing("#listbox"), "g", "a"),
            Press("pick a row", "[data-c='select'] [role=option] >> nth=2", Pressing("#listbox")),
        ]),
        ["select-bound"] = On<KitSelectFramePage>("select-bound",
        [
            .. Popup("#bound", "[data-c='select-bound'] [popover]"),
            Press("pick a row", "[data-c='select-bound'] [role=option] >> nth=2", Pressing("#bound")),
        ]),
        ["select-searchable"] = On<KitSelectFramePage>("select-searchable",
        [
            .. Popup("#searchable", "[data-c='select-searchable'] [popover]"),
            Typing("[data-c='select-searchable'] [popover] input", "al", "typing to filter", Pressing("#searchable")),
            Press("pick a row", "[data-c='select-searchable'] [role=option] >> nth=2", Pressing("#searchable")),
        ]),
        ["select-searched"] = On<KitSelectFramePage>("select-searched",
        [
            .. Popup("#searched", "[data-c='select-searched'] [popover]"),
            Typing("[data-c='select-searched'] [popover] input", "al", "typing to search", Pressing("#searched")),
            Escape(SearchingFor("#searched", "[data-c='select-searched'] [popover] input", "al"), "close by Escape after a search the page heard"),
        ]),
        ["select-combobox"] = On<KitSelectFramePage>("select-combobox",
        [
            .. Popup("#combobox", "[data-c='select-combobox'] [popover]"),
            Typing("#combobox", "al", "typing to filter"),
            Press("pick a row", "[data-c='select-combobox'] [role=option] >> nth=2", Pressing("#combobox")),
        ]),
        ["autocomplete"] = On<KitSelectFramePage>("autocomplete",
        [
            .. Popup("#autocomplete", "[data-c='autocomplete'] [popover]"),
            Typing("#autocomplete", "al", "typing to filter"),
            Press("pick an item", "[data-c='autocomplete'] [role=option] >> nth=1", Pressing("#autocomplete")),
        ]),
        ["autocomplete-bound"] = On<KitSelectFramePage>("autocomplete-bound",
        [
            .. Popup("#autocomplete-bound", "[data-c='autocomplete-bound'] [popover]"),
            Typing("#autocomplete-bound", "al", "typing to filter"),
            Press("pick an item", "[data-c='autocomplete-bound'] [role=option] >> nth=1", Pressing("#autocomplete-bound")),
        ]),
        ["pillbox"] = On<KitSelectFramePage>("pillbox",
        [
            .. Popup("#pillbox", "[data-c='pillbox'] [popover]"),
            Press("pick a row", "[data-c='pillbox'] [role=option] >> nth=1", Pressing("#pillbox")),
            Press("take a pill off", "[data-c='pillbox'] [data-rask-list-remove]"),
        ]),
        ["pillbox-combobox"] = On<KitSelectFramePage>("pillbox-combobox",
        [
            .. Popup("#pillbox-combobox", "[data-c='pillbox-combobox'] [popover]"),
            Typing("#pillbox-combobox", "be", "typing to filter"),
            Keys("Backspace over what was typed", TypingInto("#pillbox-combobox", "be"), "Backspace", "Backspace"),
        ]),

        ["chart"] = On<KitChartFramePage>("chart", Across("[data-c='chart'] svg", "pointer across the plot")),
        ["editor"] = On<KitEditorFramePage>("editor",
            Typing("[data-c='editor'] [contenteditable]", "abc"),
            TypingThenLeaving("[data-c='editor'] [contenteditable]", "abc"),
            Twice("open and close the toolbar's list of styles", "[data-c='editor'] [role=toolbar] [role=combobox]"),
            Press("press a toolbar button with nothing selected", "[data-c='editor'] [role=toolbar] button >> nth=1")),
        ["confirm-leave"] = On<KitLeaveFramePage>("confirm-leave",
            Typing("#leave-title", "abc", "typing in the guarded form"),
            new("ask to leave, be asked, and stay", async page =>
            {
                await page.ClickAsync("#leave-go");
                await page.Locator("dialog[open] button").Filter(new() { HasTextString = "Stay" }).ClickAsync();
            }, async page =>
            {
                await page.FocusAsync("#leave-title");
                await page.Keyboard.TypeAsync("abc");
            })),

        ["tree"] = On<KitOwnFramePage>("tree",
            Cursor(Focusing("[data-c='tree'] [role=tree]")),
            Press("press a node", "[data-c='tree'] [role=treeitem]")),
        ["data-grid"] = On<KitOwnFramePage>("data-grid", Press("sort a column", "[data-c='data-grid'] th button")),
        ["rating"] = On<KitOwnFramePage>("rating", Press("rate", "[data-c='rating'] input >> nth=4")),
        ["filter"] = On<KitOwnFramePage>("filter", Press("filter", "[data-c='filter'] input >> nth=1")),
        ["toggle-heard"] = On<KitOwnFramePage>("toggle-heard", Press("press", "[data-c='toggle-heard'] button")),
        ["carousel"] = On<KitOwnFramePage>("carousel"),
        ["hover-gallery"] = On<KitOwnFramePage>("hover-gallery"),
        ["fab"] = On<KitOwnFramePage>("fab"),
    };

    // A modal's own: opened by its trigger, walked, and closed each of the ways a reader closes one.
    private static KitGesture[] Dialog(string part, string trigger) =>
    [
        Press("open by a press", trigger),
        Pointer($"[data-c='{part}'] dialog", Pressing(trigger), "pointer over the open dialog"),
        Keys("Tab inside", Pressing(trigger), "Tab", "Tab", "Shift+Tab"),
        Escape(Pressing(trigger)),
        new("close by a press outside", page => page.Mouse.ClickAsync(5, 5), Pressing(trigger)),
        Press("close by its button", $"[data-c='{part}'] [aria-label='Close modal']", Pressing(trigger)),
    ];

    private static Family Whole<TPage>(params KitGesture[] own)
        where TPage : Component
    {
        KitGesture[] gestures = [Pointer(), Tab(), .. own];
        return new Family(typeof(TPage), [.. gestures.Select(gesture => gesture.Name)], playwright => KitFrameCount.OfAsync<TPage>(playwright, gestures));
    }

    // The pointer over every part and Tab through them, which every component is put through, then its own.
    private static Family On<TPage>(string part, params KitGesture[] own)
        where TPage : Component
    {
        var root = $"[data-c='{part}']";
        KitGesture[] gestures = [Pointer(root), Tab(root), .. own];
        return new Family(typeof(TPage), [.. gestures.Select(gesture => gesture.Name)], playwright => KitFrameCount.OfAsync<TPage>(playwright, gestures));
    }
}
