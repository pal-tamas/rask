using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Rask.UiTests.Flux;

/// <summary>
///     Every Rask.Ui component that mirrors a Flux UI component takes what Flux documents for it, and nothing else.
/// </summary>
/// <remarks>
///     <para>
///     <c>flux.snapshot.json</c> is Flux's reference — every component, its props, the values each accepts —
///     read from fluxui.dev's docs by <c>scripts/flux/refresh.mjs</c>. This holds each built component to it: a
///     documented prop with no property of that name, or a documented value with no enum member, fails here.
///     So does a refresh that brings a prop Flux added since.
///     </para>
///     <para>
///     What Flux says with a Livewire or Alpine attribute (<c>wire:model</c>, <c>x-on:click</c>) is said in
///     Rask with <c>Bind</c>/<c>Value</c> and a <c>Callback</c>, and is skipped by rule. Anything else that
///     does not translate is named in <see cref="NotTranslated" /> with the reason, one line each.
///     </para>
///     <para>
///     And the other way round: a property a built component declares, or a member of a prop's enum, that
///     Flux does not document fails too, unless <see cref="Translations" /> names what of Flux's it stands
///     for. A new component adds rows to <see cref="Built" /> and, where it must, to that table; no code.
///     </para>
/// </remarks>
public sealed class FluxConformanceTests
{
    /// <summary>Flux part → the Rask.Ui type that mirrors it. A component joins this when it is built.</summary>
    private static readonly Dictionary<string, Type> Built = new(StringComparer.Ordinal)
    {
        ["flux:field"] = typeof(UiField),
        ["flux:label"] = typeof(UiLabel),
        ["flux:description"] = typeof(UiDescription),
        ["flux:error"] = typeof(UiError),
        ["flux:fieldset"] = typeof(UiFieldset),
        ["flux:legend"] = typeof(UiLegend),
        ["flux:heading"] = typeof(UiHeading),
        ["flux:link"] = typeof(UiLink),
        ["flux:text"] = typeof(UiText),
        ["flux:accordion"] = typeof(UiAccordion),
        ["flux:accordion.item"] = typeof(UiAccordionItem),
        ["flux:accordion.heading"] = typeof(UiAccordionHeading),
        ["flux:accordion.content"] = typeof(UiAccordionContent),
        ["flux:callout"] = typeof(UiCallout),
        ["flux:callout.heading"] = typeof(UiCalloutHeading),
        ["flux:callout.link"] = typeof(UiCalloutLink),
        ["flux:callout.text"] = typeof(UiCalloutText),
        ["flux:button"] = typeof(UiButton),
        ["flux:button.group"] = typeof(UiButtonGroup),
        ["flux:badge"] = typeof(UiBadge),
        ["flux:badge.close"] = typeof(UiBadgeClose),
        ["flux:icon.*"] = typeof(UiIcon),
        ["flux:separator"] = typeof(UiSeparator),
        ["flux:progress"] = typeof(UiProgress),
        ["flux:skeleton"] = typeof(UiSkeleton),
        ["flux:skeleton.line"] = typeof(UiSkeletonLine),
        ["flux:skeleton.group"] = typeof(UiSkeletonGroup),
        ["flux:table"] = typeof(UiTable),
        ["flux:table.columns"] = typeof(UiTableColumns),
        ["flux:table.column"] = typeof(UiTableColumn),
        ["flux:table.rows"] = typeof(UiTableRows),
        ["flux:table.row"] = typeof(UiTableRow),
        ["flux:table.cell"] = typeof(UiTableCell),
        ["flux:card"] = typeof(UiCard),
        ["flux:card.header"] = typeof(UiCardHeader),
        ["flux:card.heading"] = typeof(UiCardHeading),
        ["flux:card.subheading"] = typeof(UiCardSubheading),
        ["flux:card.actions"] = typeof(UiCardActions),
        ["flux:card.body"] = typeof(UiCardBody),
        ["flux:card.footer"] = typeof(UiCardFooter),
        ["flux:card.bleed"] = typeof(UiCardBleed),
        ["flux:kanban"] = typeof(UiKanban),
        ["flux:kanban.column"] = typeof(UiKanbanColumn),
        ["flux:kanban.column.header"] = typeof(UiKanbanColumnHeader),
        ["flux:kanban.column.cards"] = typeof(UiKanbanColumnCards),
        ["flux:kanban.column.footer"] = typeof(UiKanbanColumnFooter),
        ["flux:kanban.card"] = typeof(UiKanbanCard),
        ["flux:toast"] = typeof(UiToast),
        ["flux:toast.group"] = typeof(UiToastGroup),
        ["flux:tooltip"] = typeof(UiTooltip),
        ["flux:tooltip.content"] = typeof(UiTooltipContent),
        ["flux:input"] = typeof(UiInput<>),
        ["flux:input.group"] = typeof(UiInputGroup),
        ["flux:input.group.prefix"] = typeof(UiInputGroupPrefix),
        ["flux:input.group.suffix"] = typeof(UiInputGroupSuffix),
        ["flux:textarea"] = typeof(UiTextarea<>),
        ["flux:select"] = typeof(UiSelect<>),
        ["flux:select.option"] = typeof(UiSelectOption),
        ["flux:select.group"] = typeof(UiSelectGroup),
        ["flux:select.option.create"] = typeof(UiSelectOptionCreate),
        ["flux:select.option.empty"] = typeof(UiSelectOptionEmpty),
        ["flux:select.button"] = typeof(UiSelectButton),
        ["flux:select.input"] = typeof(UiSelectInput),
        ["flux:select.search"] = typeof(UiSelectSearch),
        ["flux:autocomplete"] = typeof(UiAutocomplete),
        ["flux:autocomplete.item"] = typeof(UiAutocompleteItem),
        ["flux:pillbox"] = typeof(UiPillbox<>),
        ["flux:pillbox.option"] = typeof(UiPillboxOption),
        ["flux:pillbox.option.create"] = typeof(UiPillboxOptionCreate),
        ["flux:pillbox.option.empty"] = typeof(UiPillboxOptionEmpty),
        ["flux:pillbox.search"] = typeof(UiPillboxSearch),
        ["flux:pillbox.trigger"] = typeof(UiPillboxTrigger),
        ["flux:chart"] = typeof(UiChart),
        ["flux:chart.svg"] = typeof(UiChartSvg),
        ["flux:chart.line"] = typeof(UiChartLine),
        ["flux:chart.area"] = typeof(UiChartArea),
        ["flux:chart.point"] = typeof(UiChartPoint),
        ["flux:chart.pie"] = typeof(UiChartPie),
        ["flux:chart.axis"] = typeof(UiChartAxis),
        ["flux:chart.axis.mark"] = typeof(UiChartAxisMark),
        ["flux:chart.axis.line"] = typeof(UiChartAxisLine),
        ["flux:chart.axis.grid"] = typeof(UiChartAxisGrid),
        ["flux:chart.axis.tick"] = typeof(UiChartAxisTick),
        ["flux:chart.zero-line"] = typeof(UiChartZeroLine),
        ["flux:chart.tooltip"] = typeof(UiChartTooltip),
        ["flux:chart.tooltip.heading"] = typeof(UiChartTooltipHeading),
        ["flux:chart.tooltip.value"] = typeof(UiChartTooltipValue),
        ["flux:chart.tooltip.indicator"] = typeof(UiChartTooltipIndicator),
        ["flux:chart.cursor"] = typeof(UiChartCursor),
        ["flux:chart.summary"] = typeof(UiChartSummary),
        // Flux's reference spells this heading "flux:chart.summaryvalue"; its examples write flux:chart.summary.value.
        ["flux:chart.summaryvalue"] = typeof(UiChartSummaryValue),
        ["flux:chart.legend"] = typeof(UiChartLegend),
        ["flux:dropdown"] = typeof(UiDropdown),
        ["flux:menu"] = typeof(UiMenu),
        ["flux:menu.item"] = typeof(UiMenuItem),
        ["flux:menu.submenu"] = typeof(UiMenuSubmenu),
        ["flux:menu.separator"] = typeof(UiMenuSeparator),
        ["flux:menu.checkbox.group"] = typeof(UiMenuCheckboxGroup),
        ["flux:menu.checkbox"] = typeof(UiMenuCheckbox),
        ["flux:menu.radio.group"] = typeof(UiMenuRadioGroup<>),
        ["flux:menu.radio"] = typeof(UiMenuRadio),
        ["flux:context"] = typeof(UiContext),
        ["flux:checkbox"] = typeof(UiCheckbox),
        ["flux:checkbox.group"] = typeof(UiCheckboxGroup<>),
        ["flux:checkbox.all"] = typeof(UiCheckboxAll),
        ["flux:radio.group"] = typeof(UiRadioGroup<>),
        ["flux:radio"] = typeof(UiRadio),
        ["flux:radio.indicator"] = typeof(UiRadioIndicator),
        ["flux:switch"] = typeof(UiSwitch),
        ["flux:modal"] = typeof(UiModal),
        ["flux:modal.trigger"] = typeof(UiModalTrigger),
        ["flux:modal.close"] = typeof(UiModalClose),
        ["flux:editor"] = typeof(UiEditor),
        ["flux:editor.toolbar"] = typeof(UiEditorToolbar),
        ["flux:editor.button"] = typeof(UiEditorButton),
        ["flux:editor.content"] = typeof(UiEditorContent),
        // `paginator` is a UiPaginator: what Laravel's paginator object knows, as a value.
        ["flux:pagination"] = typeof(UiPagination),
        ["flux:timeline"] = typeof(UiTimeline),
        ["flux:timeline.item"] = typeof(UiTimelineItem),
        ["flux:timeline.indicator"] = typeof(UiTimelineIndicator),
        ["flux:timeline.content"] = typeof(UiTimelineContent),
        ["flux:timeline.block"] = typeof(UiTimelineBlock),
        ["flux:timeline.subgrid"] = typeof(UiTimelineSubgrid),
        ["flux:calendar"] = typeof(UiCalendarControl<>),
        ["flux:date-picker"] = typeof(UiDatePickerControl<>),
        ["flux:date-picker.input"] = typeof(UiDatePickerInput),
        ["flux:date-picker.button"] = typeof(UiDatePickerButton),
        ["flux:time-picker"] = typeof(UiTimePicker<>),
    };

    /// <summary><c>part/prop</c> or <c>part/prop=value</c> → why Rask.Ui does not carry it.</summary>
    private static readonly Dictionary<string, string> NotTranslated = new(StringComparer.Ordinal)
    {
        ["flux:error/bag"] = "Laravel's named error bags. A Rask form has one edit context, and Ui.Error reads that one.",
        ["flux:error/deep"] = "Laravel's dotted paths (fields.*). A Rask field is the member of the object that owns it: Ui.Error.For(() => order.Lines[0].Name).",
        ["flux:heading/size=2xl"] = "an identifier cannot start with a digit: Ui.HeadingSize.Xxl",
        ["flux:text/color=default"] = "no colour is an unset Color; Ui.Color holds Tailwind's hues only",
        ["flux:text/size=base"] = "the heading page's name for the text page's `default`: Ui.TextSize.Default",
        ["flux:button/as-an-input"] = "A section of the button page that shows flux:input drawn as a button; it is the input's to mirror.",
        ["flux:badge/variant=pill"] = "deprecated by Flux itself in favour of the `rounded` prop: Ui.Badge.Rounded()",
        ["flux:badge/variant=rounded"] = "not a value: the docs' deprecation note for `pill` names the `rounded` prop, and the snapshot read it as an option",
        // The popover page's prop: a panel that opens while the pointer rests on its trigger. CSS cannot open a
        // [popover] and the runtime has no hook that does, so it is built with Ui.Popover, on Flux's popover page.
        ["flux:dropdown/hover"] = "Opening on hover needs a runtime hook that shows a popover on pointerenter; it belongs to the popover page.",
        ["flux:menu.checkbox/checked"] = "A Rask control's state is its Value, or the model it is bound to: Ui.MenuCheckbox.Value(true) / .Bind(() => filter.Draft).",
        // Sections of the icon page rather than props, recorded here so the omission is a decision.
        ["flux:icon.*/lucide-icons"] = "`php artisan flux:icon` copies Lucide SVGs into a Laravel project as Blade files; Ui.IconName is a closed, generated set.",
        ["flux:icon.*/custom-icons"] = "A Blade file under resources/views/flux/icon. In Rask a custom icon is an ordinary component drawing its own Svg.",
        ["flux:kanban.column.header/badge"] = "No example on Flux's page draws it, so where it sits and how it looks cannot be measured; a badge of your own goes in as a child, beside the heading you write there.",
        ["flux:editor/custom-items"] = "A Blade file under resources/views/flux/editor, named in `toolbar`. In Rask an item of the app's own is composed into Ui.EditorToolbar[…] as a Ui.EditorButton.",
        ["flux:editor/extensions"] = "`flux:editor` is a DOM event; the kit raises it as `ui:editor` on the editor, with the same registerExtension(s) / enableExtension / disableExtension / init.",
        ["flux:table/pagination:scroll-to"] = "Paginate takes the pager itself, not a paginator the table draws one from: where a page change scrolls to is that pager's own prop",
        ["flux:input/mask:dynamic"] = "An Alpine expression evaluated in the browser on every keystroke. The runtime shapes an amount (`data-rask-mask-money`, Flux's `$money($input)`) and nothing else; what a C# prop for it takes is not decided, so Mask takes the static pattern only.",
        ["flux:autocomplete/mask:dynamic"] = "The input's: see flux:input/mask:dynamic.",
        ["flux:select.option/avatar:*"] = "Props forwarded to Flux's avatar. Ui.Avatar is not Flux's yet; an option draws the extra-small round avatar Flux draws there.",
        ["flux:select.option.create/modal"] = "Opens a Flux modal by its name through Flux's script. The row's OnClick is the page's to answer, and opening a modal is one answer.",
        ["flux:pillbox.option.create/modal"] = "Opens a Flux modal by its name through Flux's script. The row's OnClick is the page's to answer, and opening a modal is one answer.",
        ["flux:switch/align=right|start"] = "Two spellings of one side, as the reference lists them. Ui.SwitchAlign.Right is it; `start` is not a second value.",
        ["flux:switch/align=left|end"] = "Two spellings of one side. Ui.SwitchAlign.Left is it; `end` is not a second value.",
        // Flux's imperative API. A page opens a modal by rendering it open, and the browser by a trigger's command.
        ["Flux::modal()"] = "Flux::modal('confirm')->show()/close() from PHP: Rask's page owns the state — Ui.Modal.Open(_confirming) — or holds no state at all behind a Ui.ModalTrigger.",
        ["Flux::modals()"] = "Closes every modal on the page from PHP. Each Rask modal's open state is its own page's field; there is no registry to sweep.",
        ["$flux.modal()"] = "Alpine's magic: the kit ships no script. A button that opens or closes a named modal is Ui.ModalTrigger / Ui.ModalClose, which write the browser's own invoker commands.",
        ["flux:calendar/start-day=0"] = "A number in Flux; a DayOfWeek here: StartDay(DayOfWeek.Sunday).",
        ["flux:calendar/start-day=6"] = "A number in Flux; a DayOfWeek here: StartDay(DayOfWeek.Saturday).",
        ["flux:calendar/size=2xl"] = "an identifier cannot start with a digit: Ui.CalendarSize.Xxl",
        ["flux:calendar/with-inputs"] = "No example on Flux's public pages draws a calendar with its inputs, so there is nothing to measure it from.",
        ["flux:date-picker/start-day=0"] = "A number in Flux; a DayOfWeek here: StartDay(DayOfWeek.Sunday).",
        ["flux:date-picker/start-day=6"] = "A number in Flux; a DayOfWeek here: StartDay(DayOfWeek.Saturday).",
        ["flux:date-picker/size=2xl"] = "an identifier cannot start with a digit: Ui.DatePickerSize.Xxl",
        ["flux:date-picker/with-inputs"] = "No example on Flux's public pages draws the calendar with its inputs, so there is nothing to measure it from.",
        ["flux:date-picker/clearable"] = "No example on Flux's public pages draws the clear button, so there is nothing to measure it from.",
        ["flux:date-picker.input/clearable"] = "No example on Flux's public pages draws the clear button, so there is nothing to measure it from.",
        ["flux:date-picker.button/clearable"] = "No example on Flux's public pages draws the clear button, so there is nothing to measure it from.",
        ["flux:date-picker.input/variant"] = "The typed field is drawn one way, Flux's `custom` — its recommended and future default. `native` is on no public example to measure.",
        ["flux:date-picker.input/placeholder"] = "The typed field's placeholders are its segments' own: mm, dd, yyyy in the locale's order.",
        ["flux:time-picker/multiple"] = "The bound type says it: a collection of TimeOnly (List<TimeOnly>, TimeOnly[], HashSet<TimeOnly>) is several times, and a form's model has to state its shape anyway. Ui.TimePicker.Of<List<TimeOnly>>() opens one with no value yet.",
        ["flux:time-picker/time-format=12-hour"] = "an identifier cannot start with a digit: Ui.TimePickerTimeFormat.TwelveHour",
        ["flux:time-picker/time-format=24-hour"] = "an identifier cannot start with a digit: Ui.TimePickerTimeFormat.TwentyFourHour",
        ["flux:time-picker/min=now"] = "A shorthand the browser's clock answers. Min and Max take a TimeOnly; the page passes TimeOnly.FromDateTime(...) from the clock it trusts.",
        ["flux:time-picker/max=now"] = "As min=now.",
    };

    /// <summary>
    ///     <c>part/Member</c> or <c>part/Member=EnumMember</c> → what of Flux's that member is Rask's way of saying.
    ///     The ONLY things a built component may carry that Flux's reference does not list. Not a place for a
    ///     convenience: a row names something Flux HAS — a directive, a slot, an event, an attribute it forwards —
    ///     or it is not a row.
    /// </summary>
    private static readonly Dictionary<string, string> Translations = new(StringComparer.Ordinal)
    {
        ["flux:label/For"] = "the `for` of the <label> that stands in for <ui-label>, which finds its control by script",
        ["flux:error/For"] = "`name`, as the expression a Rask form binds by: Ui.Error.For(() => order.Email)",
        ["flux:heading/Size=Xxl"] = "`2xl`: an identifier cannot start with a digit",
        ["flux:link/Accent"] = "`:accent=\"false\"`, which Flux documents on its theming page and not in the link's reference",
        ["flux:text/Color=Amber"] = "Ui.Color is one enum for every `color` prop; Flux's text reference lists the other sixteen hues",
        ["flux:text/Color=Slate"] = "Ui.Color is one enum for every `color` prop; the text draws a neutral as Flux's `default`",
        ["flux:text/Color=Gray"] = "as Slate",
        ["flux:text/Color=Zinc"] = "as Slate",
        ["flux:text/Color=Neutral"] = "as Slate",
        ["flux:text/Color=Stone"] = "as Slate",
        ["flux:icon.*/Name"] = "the `*` of `flux:icon.*`: which icon",
        ["flux:callout/CustomIcon"] = "the `icon` slot; `Icon` is the prop of that name",
        ["flux:callout.heading/CustomIcon"] = "the `icon` slot; `Icon` is the prop of that name",
        ["flux:callout/Role"] = "an attribute Flux forwards to the root; the callout is not a UiElement, so it declares the one it takes",
        ["flux:skeleton/Style"] = "an attribute Flux forwards to the root; the skeleton is not a UiElement, so it declares the one it takes",
        ["flux:skeleton.line/Style"] = "as flux:skeleton",
        ["flux:skeleton.group/Style"] = "as flux:skeleton",
        ["flux:button/Inset=All"] = "the bare `inset` of Flux's own example: every side",
        ["flux:badge/Inset=All"] = "Ui.Inset is the button's too, where it is the bare `inset`",
        ["flux:button/Disabled"] = "the <button>'s own `disabled`, which Flux forwards",
        ["flux:button/Command"] = "the <button>'s own `command`, which Flux forwards",
        ["flux:button/CommandFor"] = "the <button>'s own `commandfor`, which Flux forwards",
        ["flux:chart.svg/Gutter"] = "`gutter`, which Flux documents under \"Chart padding\" and not in the reference",
        ["flux:chart.svg/Width"] = "the box Flux measures off its element in the browser, by script; a chart drawn in C# is told it",
        ["flux:chart.svg/Height"] = "as Width",
        ["flux:chart.line/StrokeDasharray"] = "an SVG attribute Flux forwards to the element it draws; the part is a declaration, not an element, so it names the ones Flux's page shows",
        ["flux:chart.point/R"] = "an SVG attribute Flux forwards to the element it draws; the part is a declaration, not an element, so it names the ones Flux's page shows",
        ["flux:chart.point/StrokeWidth"] = "an SVG attribute Flux forwards to the element it draws; the part is a declaration, not an element, so it names the ones Flux's page shows",
        ["flux:chart.pie/ColorField"] = "the `color` key Flux reads off a row, as a selector",
        ["flux:chart.axis/Position"] = "`position`, which Flux's area example sets and its reference omits",
        ["flux:chart.axis/TickCount"] = "`tick-count`, which Flux documents under \"Tick frequency\" and not in the reference",
        ["flux:chart.axis/TickStart"] = "`tick-start`, as TickCount",
        ["flux:chart.axis/TickEnd"] = "`tick-end`, as TickCount",
        ["flux:chart.axis/TickValues"] = "`tick-values`, as TickCount",
        ["flux:chart.axis/TickPrefix"] = "`tick-prefix`, which Flux documents under \"Tick formatting\" and not in the reference",
        ["flux:chart.axis/TickSuffix"] = "`tick-suffix`, as TickPrefix",
        ["flux:chart.axis.mark/StrokeWidth"] = "an SVG attribute Flux forwards to the element it draws; the part is a declaration, not an element, so it names the ones Flux's page shows",
        ["flux:chart.axis.line/StrokeWidth"] = "an SVG attribute Flux forwards to the element it draws; the part is a declaration, not an element, so it names the ones Flux's page shows",
        ["flux:modal/Open"] = "`wire:model`: the page's own bool, which the runtime shows and closes the dialog by (data-rask-modal-open)",
        ["flux:modal/OnClose"] = "the `close` event (`@close`)",
        ["flux:modal/OnCancel"] = "the `cancel` event (`@cancel`)",
        ["flux:chart.axis.grid/StrokeWidth"] = "an SVG attribute Flux forwards to the element it draws; the part is a declaration, not an element, so it names the ones Flux's page shows",
        ["flux:chart.axis.grid/StrokeDasharray"] = "an SVG attribute Flux forwards to the element it draws; the part is a declaration, not an element, so it names the ones Flux's page shows",
        ["flux:chart.zero-line/StrokeWidth"] = "an SVG attribute Flux forwards to the element it draws; the part is a declaration, not an element, so it names the ones Flux's page shows",
        ["flux:chart.tooltip.value/Prefix"] = "`prefix`, which Flux's bar example sets and its reference omits",
        ["flux:chart.tooltip.value/Suffix"] = "`suffix`, which Flux's pie examples set and its reference omits",
        ["flux:chart.cursor/Type"] = "`type=\"area\"`, which Flux's bar examples set and its reference omits",
        ["flux:chart.cursor/StrokeWidth"] = "an SVG attribute Flux forwards to the element it draws; the part is a declaration, not an element, so it names the ones Flux's page shows",
        ["flux:chart.cursor/StrokeDasharray"] = "an SVG attribute Flux forwards to the element it draws; the part is a declaration, not an element, so it names the ones Flux's page shows",
        ["flux:table.column/OnSort"] = "`wire:click=\"sort('…')\"` on a sortable column",
        // The five form controls: `wire:model` is Bind (or Value with OnChange), and what a Livewire component
        // does around it — rules, an `updated` hook, where its message shows — is said on the control.
        ["flux:input/Bind"] = "`wire:model`, two-way",
        ["flux:input/Value"] = "`wire:model` read one way (or the `value` attribute Flux forwards); OnChange is the other way",
        ["flux:input/OnChange"] = "`wire:model`'s write, handed to the parent",
        ["flux:input/Validate"] = "the Livewire component's rule for the `wire:model` property",
        ["flux:input/AfterBind"] = "Livewire's `updated…` hook of the `wire:model` property",
        ["flux:input/ShowValidation"] = "whether the shorthand field draws its `flux:error`: false is Flux's control written inside a `flux:field` of your own",
        ["flux:textarea/Bind"] = "`wire:model`, two-way",
        ["flux:textarea/Value"] = "`wire:model` read one way (or the `value` attribute Flux forwards); OnChange is the other way",
        ["flux:textarea/OnChange"] = "`wire:model`'s write, handed to the parent",
        ["flux:textarea/Validate"] = "the Livewire component's rule for the `wire:model` property",
        ["flux:textarea/AfterBind"] = "Livewire's `updated…` hook of the `wire:model` property",
        ["flux:textarea/ShowValidation"] = "whether the shorthand field draws its `flux:error`: false is Flux's control written inside a `flux:field` of your own",
        ["flux:select/Bind"] = "`wire:model`, two-way",
        ["flux:select/Value"] = "`wire:model` read one way (or the `value` attribute Flux forwards); OnChange is the other way",
        ["flux:select/OnChange"] = "`wire:model`'s write, handed to the parent",
        ["flux:select/Validate"] = "the Livewire component's rule for the `wire:model` property",
        ["flux:select/AfterBind"] = "Livewire's `updated…` hook of the `wire:model` property",
        ["flux:select/ShowValidation"] = "whether the shorthand field draws its `flux:error`: false is Flux's control written inside a `flux:field` of your own",
        ["flux:autocomplete/Bind"] = "`wire:model`, two-way",
        ["flux:autocomplete/Value"] = "`wire:model` read one way (or the `value` attribute Flux forwards); OnChange is the other way",
        ["flux:autocomplete/OnChange"] = "`wire:model`'s write, handed to the parent",
        ["flux:autocomplete/Validate"] = "the Livewire component's rule for the `wire:model` property",
        ["flux:autocomplete/AfterBind"] = "Livewire's `updated…` hook of the `wire:model` property",
        ["flux:autocomplete/ShowValidation"] = "whether the shorthand field draws its `flux:error`: false is Flux's control written inside a `flux:field` of your own",
        ["flux:pillbox/Bind"] = "`wire:model`, two-way",
        ["flux:pillbox/Value"] = "`wire:model` read one way (or the `value` attribute Flux forwards); OnChange is the other way",
        ["flux:pillbox/OnChange"] = "`wire:model`'s write, handed to the parent",
        ["flux:pillbox/Validate"] = "the Livewire component's rule for the `wire:model` property",
        ["flux:pillbox/AfterBind"] = "Livewire's `updated…` hook of the `wire:model` property",
        ["flux:pillbox/ShowValidation"] = "whether the shorthand field draws its `flux:error`: false is Flux's control written inside a `flux:field` of your own",
        ["flux:input/Type"] = "the <input>'s own `type`, which Flux forwards and its examples set (`type=\"email\"`); flux:autocomplete documents it",
        ["flux:input/Attributes"] = "the attributes Flux forwards to the <input> (`aria-label`, `required`, `autocomplete`…): the input is not a UiElement, so it declares the bag",
        ["flux:input/Min"] = "the <input>'s own `min`, which Flux forwards",
        ["flux:input/Max"] = "the <input>'s own `max`, which Flux forwards (`max=\"2999-12-31\"` in its own example)",
        ["flux:input/Step"] = "the <input>'s own `step`, which Flux forwards",
        ["flux:input/MaxLength"] = "the <input>'s own `maxlength`, which Flux forwards",
        ["flux:input/Autofocus"] = "the <input>'s own `autofocus`, which Flux forwards",
        ["flux:input/Name"] = "the <input>'s own `name`, which Flux forwards",
        ["flux:input/Ref"] = "`x-ref` on the <input>: the handle code focuses or measures it by",
        ["flux:input/OnInput"] = "`wire:model.live`: every keystroke, as text",
        ["flux:input/OnFiles"] = "`wire:model` on a file input: the chosen files",
        ["flux:input/OnClick"] = "`wire:click` on the input drawn `as=\"button\"`",
        ["flux:textarea/Disabled"] = "the <textarea>'s own `disabled`, which Flux forwards",
        ["flux:textarea/ReadOnly"] = "the <textarea>'s own `readonly`, which Flux forwards",
        ["flux:textarea/OnInput"] = "`wire:model.live`: every keystroke, as text",
        ["flux:select/Name"] = "the control's own `name`, which Flux forwards: what the answer posts under from a plain form",
        ["flux:select.option.create/OnClick"] = "`wire:click`, which the reference lists as a directive",
        ["flux:select.input/Value"] = "`wire:model.live=\"search\"` on `flux:select.input` in Flux's dynamic-options example",
        ["flux:select.input/OnInput"] = "as Value: the write of that `wire:model.live`",
        ["flux:select.search/Value"] = "`wire:model.live` on the search field, as Flux's dynamic-options example writes it on `flux:select.input`: a list the page filters itself (`:filter=\"false\"`)",
        ["flux:select.search/OnInput"] = "as Value: the write of that `wire:model.live`",
        ["flux:pillbox/Variant"] = "`variant=\"combobox\"`, which every combobox example on Flux's page writes and its reference omits",
        ["flux:pillbox.option.create/OnClick"] = "`wire:click`, which the reference lists as a directive",
        ["flux:pillbox.search/Value"] = "as flux:select.search",
        ["flux:pillbox.search/OnInput"] = "as Value: the write of that `wire:model.live`",
        ["flux:dropdown/Open"] = "`wire:model`, which the popover page documents for flux:dropdown: the open state",
        ["flux:dropdown/OnToggle"] = "`wire:model`'s other half: the reader opened or closed it",
        ["flux:context/Open"] = "`wire:model`: the context menu's open state",
        ["flux:context/OnToggle"] = "`wire:model`'s other half: the reader opened or closed it",
        ["flux:menu.item/OnClick"] = "`wire:click` on a row",
        // Flux's public demo, https://fluxui.dev/demo/qa.md: <flux:menu.item href="/settings/profile" icon="cog">, live an <a role="menuitem">.
        ["flux:menu.item/Href"] = "`href`, which Flux's Q&A demo sets on a menu item (drawn as an `<a role=\"menuitem\">`) and its reference omits",
        ["flux:menu.checkbox/Value"] = "`wire:model`, as every Rask form control says it (IFormControl<bool>)",
        ["flux:menu.checkbox/OnChange"] = "as Value",
        ["flux:menu.checkbox/Bind"] = "as Value",
        ["flux:menu.checkbox/Validate"] = "as Value",
        ["flux:menu.checkbox/AfterBind"] = "as Value",
        ["flux:menu.radio.group/Value"] = "`wire:model`, as every Rask form control says it (IFormControl<T>)",
        ["flux:menu.radio.group/OnChange"] = "as Value",
        ["flux:menu.radio.group/Bind"] = "as Value",
        ["flux:menu.radio.group/Validate"] = "as Value",
        ["flux:menu.radio.group/AfterBind"] = "as Value",
        ["flux:menu.radio/Value"] = "the `value` attribute a radio is matched by in a `wire:model` group",
        ["flux:menu.radio/OnClick"] = "`wire:click` on a radio that is in no `wire:model` group",
        // The checkbox, the radio and the switch. A checkbox documents `value` and `checked` itself, so its own
        // `wire:model` has no `Value` row; a radio binds nothing — its group does.
        ["flux:checkbox/Bind"] = "`wire:model`, two-way",
        ["flux:checkbox/OnChange"] = "`wire:model`'s write, handed to the parent",
        ["flux:checkbox/Validate"] = "the Livewire component's rule for the `wire:model` property",
        ["flux:checkbox/AfterBind"] = "Livewire's `updated…` hook of the `wire:model` property",
        ["flux:checkbox/ShowValidation"] = "whether the shorthand field draws its `flux:error`: false is Flux's control written inside a `flux:field` of your own",
        ["flux:checkbox.group/Bind"] = "`wire:model`, two-way",
        ["flux:checkbox.group/Value"] = "`wire:model` read one way; OnChange is the other way",
        ["flux:checkbox.group/OnChange"] = "`wire:model`'s write, handed to the parent",
        ["flux:checkbox.group/Validate"] = "the Livewire component's rule for the `wire:model` property",
        ["flux:checkbox.group/AfterBind"] = "Livewire's `updated…` hook of the `wire:model` property",
        ["flux:checkbox.group/ShowValidation"] = "whether the shorthand field draws its `flux:error`: false is Flux's control written inside a `flux:field` of your own",
        ["flux:radio.group/Bind"] = "`wire:model`, two-way",
        ["flux:radio.group/Value"] = "`wire:model` read one way; OnChange is the other way",
        ["flux:radio.group/OnChange"] = "`wire:model`'s write, handed to the parent",
        ["flux:radio.group/Validate"] = "the Livewire component's rule for the `wire:model` property",
        ["flux:radio.group/AfterBind"] = "Livewire's `updated…` hook of the `wire:model` property",
        ["flux:radio.group/ShowValidation"] = "whether the shorthand field draws its `flux:error`: false is Flux's control written inside a `flux:field` of your own",
        ["flux:switch/Bind"] = "`wire:model`, two-way",
        ["flux:switch/Value"] = "`wire:model` read one way; OnChange is the other way",
        ["flux:switch/OnChange"] = "`wire:model`'s write, handed to the parent",
        ["flux:switch/Validate"] = "the Livewire component's rule for the `wire:model` property",
        ["flux:switch/AfterBind"] = "Livewire's `updated…` hook of the `wire:model` property",
        ["flux:switch/ShowValidation"] = "whether the shorthand field draws its `flux:error`: false is Flux's control written inside a `flux:field` of your own",
        ["flux:checkbox/Icon"] = "`icon`, which Flux's \"Cards with icons\" and \"Buttons\" examples set and its reference omits",
        ["flux:checkbox/Attributes"] = "the attributes Flux forwards to the control (`name`, as its radio example writes; `aria-label`, `required`): the root is a <label> around the real <input>, which is where they belong, so the control declares the bag",
        ["flux:radio/Attributes"] = "the attributes Flux forwards to the control (`name`, as its radio example writes; `aria-label`, `required`): the root is a <label> around the real <input>, which is where they belong, so the control declares the bag",
        ["flux:switch/Attributes"] = "the attributes Flux forwards to the control (`name`, as its radio example writes; `aria-label`, `required`): the root is a <label> around the real <input>, which is where they belong, so the control declares the bag",
        ["flux:radio.group/Size"] = "`size=\"sm\"`, which Flux documents under \"Segmented\" and not in the reference",
        ["flux:radio.group/Indicator"] = "`:indicator=\"false\"`, which Flux documents under \"Cards without indicators\" and not in the reference",
        ["flux:switch/Align=Left"] = "`left|end`, which the reference lists as one option with two spellings",
        ["flux:editor/Bind"] = "`wire:model`, as the expression a Rask form binds by: Ui.Editor.Bind(() => post.Body)",
        ["flux:editor/OnChange"] = "the change `wire:model` listens for: the editor's HTML, each time it changes",
        ["flux:editor.button/IconVariant=Solid"] = "Ui.IconVariant is one enum for every icon; Flux's reference lists mini, micro and outline for this button",
        ["flux:editor.button/OnClick"] = "`wire:click` / `x-on:click`, which Flux forwards to the <button>",
        // A UiPaginator stands in for the Laravel paginator object Flux's `paginator` prop is handed: Page,
        // PerPage and Total are what `paginate()` knows, HasMore what `simplePaginate()` knows.
        ["flux:pagination/OnPage"] = "the `wire:click` of each page button Flux draws inside a Livewire component: the page chosen, counted from one",
        ["flux:pagination/Href"] = "the page URLs a Laravel paginator carries, which Flux's links follow outside Livewire: a RouteUrl per page",
        ["flux:timeline.indicator/Color=Slate"] = "Ui.Color is one enum for every `color` prop; an indicator draws a neutral as Flux's plain one",
        ["flux:timeline.indicator/Color=Gray"] = "as Slate",
        ["flux:timeline.indicator/Color=Zinc"] = "as Slate",
        ["flux:timeline.indicator/Color=Neutral"] = "as Slate",
        ["flux:timeline.indicator/Color=Stone"] = "as Slate",
        // The calendar, the date picker and the time picker.
        ["flux:calendar/Size=Xxl"] = "`2xl`: an identifier cannot start with a digit",
        ["flux:calendar/StartDay=Monday"] = "`start-day=\"1\"`: a DayOfWeek where Flux takes its number",
        ["flux:calendar/StartDay=Tuesday"] = "`start-day=\"2\"`: a DayOfWeek where Flux takes its number",
        ["flux:calendar/StartDay=Wednesday"] = "`start-day=\"3\"`: a DayOfWeek where Flux takes its number",
        ["flux:calendar/StartDay=Thursday"] = "`start-day=\"4\"`: a DayOfWeek where Flux takes its number",
        ["flux:calendar/StartDay=Friday"] = "`start-day=\"5\"`: a DayOfWeek where Flux takes its number",
        ["flux:calendar/StartDay=Saturday"] = "`start-day=\"6\"`: a DayOfWeek where Flux takes its number",
        ["flux:calendar/FixedWeeks"] = "`fixed-weeks`: in the calendar page's 'Fixed weeks' example (`<flux:calendar fixed-weeks />`), missing from its reference",
        ["flux:calendar/Bind"] = "`wire:model`, two-way",
        ["flux:calendar/OnChange"] = "`wire:model`'s write, handed to the parent",
        ["flux:calendar/Validate"] = "the Livewire component's rule for the `wire:model` property",
        ["flux:calendar/AfterBind"] = "Livewire's `updated…` hook of the `wire:model` property",
        ["flux:date-picker/Size=Xxl"] = "`2xl`: an identifier cannot start with a digit",
        ["flux:date-picker/StartDay=Monday"] = "`start-day=\"1\"`: a DayOfWeek where Flux takes its number",
        ["flux:date-picker/StartDay=Tuesday"] = "`start-day=\"2\"`: a DayOfWeek where Flux takes its number",
        ["flux:date-picker/StartDay=Wednesday"] = "`start-day=\"3\"`: a DayOfWeek where Flux takes its number",
        ["flux:date-picker/StartDay=Thursday"] = "`start-day=\"4\"`: a DayOfWeek where Flux takes its number",
        ["flux:date-picker/StartDay=Friday"] = "`start-day=\"5\"`: a DayOfWeek where Flux takes its number",
        ["flux:date-picker/StartDay=Saturday"] = "`start-day=\"6\"`: a DayOfWeek where Flux takes its number",
        ["flux:date-picker/FixedWeeks"] = "`fixed-weeks`: in the date picker page's 'Fixed weeks' example (`<flux:date-picker fixed-weeks />`), missing from its reference",
        ["flux:date-picker/Bind"] = "`wire:model`, two-way",
        ["flux:date-picker/OnChange"] = "`wire:model`'s write, handed to the parent",
        ["flux:date-picker/Validate"] = "the Livewire component's rule for the `wire:model` property",
        ["flux:date-picker/AfterBind"] = "Livewire's `updated…` hook of the `wire:model` property",
        ["flux:date-picker/ShowValidation"] = "whether the shorthand field draws its `flux:error`: false is Flux's control written inside a `flux:field` of your own",
        ["flux:time-picker/TimeFormat=TwelveHour"] = "`12-hour`: an identifier cannot start with a digit",
        ["flux:time-picker/TimeFormat=TwentyFourHour"] = "`24-hour`: an identifier cannot start with a digit",
        ["flux:time-picker/Dropdown"] = "`:dropdown='false'`: in the time picker page's 'Without dropdown' example, missing from its reference",
        ["flux:time-picker/Bind"] = "`wire:model`, two-way",
        ["flux:time-picker/OnChange"] = "`wire:model`'s write, handed to the parent",
        ["flux:time-picker/Validate"] = "the Livewire component's rule for the `wire:model` property",
        ["flux:time-picker/AfterBind"] = "Livewire's `updated…` hook of the `wire:model` property",
        ["flux:time-picker/ShowValidation"] = "whether the shorthand field draws its `flux:error`: false is Flux's control written inside a `flux:field` of your own",
    };

    /// <summary>What every component takes, Flux's included: its classes, its identity, what is inside it.</summary>
    private static readonly HashSet<string> Everywhere = new(StringComparer.Ordinal) { "Class", "Key", "Id", "Children" };

    private static readonly BindingFlags Public = BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

    [Fact]
    public void The_snapshot_holds_the_whole_catalogue()
    {
        var parts = Parts().Select(part => part.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(53, Snapshot.RootElement.GetProperty("pages").GetArrayLength());
        Assert.All(Built.Keys, part => Assert.Contains(part, parts));
    }

    [Fact]
    public void Every_built_component_takes_what_Flux_documents()
    {
        var missing = new List<string>();

        foreach (var (part, props) in Parts().Where(part => Built.ContainsKey(part.Name)))
        {
            foreach (var prop in props.Where(prop => !IsDirective(prop.Name) && !NotTranslated.ContainsKey($"{part}/{prop.Name}")))
            {
                var property = Built[part].GetProperty(Pascal(prop.Name), Public | BindingFlags.IgnoreCase);
                if (property is null)
                {
                    missing.Add($"{part}/{prop.Name}: no {Built[part].Name}.{Pascal(prop.Name)}");
                    continue;
                }

                var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                if (!type.IsEnum)
                {
                    continue;
                }

                missing.AddRange(prop.Options
                    .Where(option => !NotTranslated.ContainsKey($"{part}/{prop.Name}={option}"))
                    .Where(option => !Enum.GetNames(type).Contains(Pascal(option), StringComparer.OrdinalIgnoreCase))
                    .Select(option => $"{part}/{prop.Name}={option}: no {type.Name}.{Pascal(option)}"));
            }
        }

        Assert.True(missing.Count == 0, "Flux documents these and Rask.Ui does not take them:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void A_built_component_carries_nothing_Flux_does_not_document()
    {
        var carried = new List<string>();

        foreach (var (part, component) in Built)
        {
            // A part two pages document (flux:text, on the heading page too) takes what either says.
            var props = Parts().Where(documented => documented.Name == part).SelectMany(documented => documented.Props).ToList();
            foreach (var property in OwnProps(component))
            {
                var options = props.Where(prop => Same(prop.Name, property.Name)).Select(prop => prop.Options).ToList();
                carried.AddRange(options.Count == 0 && !Slots(part).Any(slot => Same(slot, property.Name))
                    ? [$"{part}/{property.Name}"]
                    : OwnValues(property, [.. options.SelectMany(values => values)]).Select(value => $"{part}/{property.Name}={value}"));
            }
        }

        var added = carried.Except(Translations.Keys, StringComparer.Ordinal).ToList();
        var stale = Translations.Keys.Except(carried, StringComparer.Ordinal).ToList();
        Assert.True(added.Count == 0,
            "Rask.Ui carries these and Flux does not document them. Delete the member and convert its call sites. Only if it is "
            + "how Rask says something Flux HAS (a directive, a slot, an event, an attribute it forwards), add a `Translations` "
            + "row `part/Member` or `part/Member=EnumMember` that names it:\n  " + string.Join("\n  ", added));
        Assert.True(stale.Count == 0,
            "`Translations` explains these, and the component no longer carries them (or Flux documents them now). "
            + "Delete the row:\n  " + string.Join("\n  ", stale));
    }

    private static JsonDocument Snapshot { get; } = JsonDocument.Parse(File.ReadAllText(
        Path.Combine(RepoRoot.FullPath, "tests", "Rask.Ui.Tests", "Flux", "flux.snapshot.json")));

    private static IEnumerable<(string Name, List<(string Name, string[] Options)> Props)> Parts() =>
        from page in Snapshot.RootElement.GetProperty("pages").EnumerateArray()
        from part in page.GetProperty("parts").EnumerateArray()
        select (
            part.GetProperty("name").GetString()!,
            part.TryGetProperty("props", out var props)
                ? props.EnumerateArray().Select(prop => (
                    prop.GetProperty("name").GetString()!,
                    prop.TryGetProperty("options", out var options)
                        ? options.EnumerateArray().Select(option => option.GetString()!).ToArray()
                        : [])).ToList()
                : []);

    // `<x-slot name="actions">`: a named slot is a property of that name, holding a component.
    private static IEnumerable<string> Slots(string name) =>
        from page in Snapshot.RootElement.GetProperty("pages").EnumerateArray()
        from part in page.GetProperty("parts").EnumerateArray()
        where part.GetProperty("name").GetString() == name && part.TryGetProperty("slots", out _)
        from slot in part.GetProperty("slots").EnumerateArray()
        select slot.GetProperty("name").GetString()!;

    // What a call site can set and the component itself declares. What UiElement, Element and Component hand
    // down is every component's: Flux forwards any HTML attribute to its root.
    private static IEnumerable<PropertyInfo> OwnProps(Type component) =>
        component.GetProperties(Public)
            .Where(property => property.SetMethod is { IsPublic: true })
            .Where(property => property.DeclaringType != typeof(UiElement) && property.DeclaringType!.Assembly != typeof(Rask.Core.Component).Assembly)
            .Where(property => !Everywhere.Contains(property.Name));

    // The members of a prop's enum that are not values Flux lists for it. The zero member is the unset prop,
    // which an enum has to name and Flux often does not. A prop Flux lists no values for (`color`, `icon`) is
    // not checked.
    private static IEnumerable<string> OwnValues(PropertyInfo property, string[] options)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        return type.IsEnum && options.Length > 0
            ? Enum.GetNames(type).Where(member => !IsZero(type, member) && !options.Any(option => Same(option, member)))
            : [];
    }

    private static bool IsZero(Type type, string member) => Convert.ToInt64(Enum.Parse(type, member), CultureInfo.InvariantCulture) == 0;

    private static bool Same(string flux, string member) => string.Equals(Pascal(flux), member, StringComparison.OrdinalIgnoreCase);

    // wire:model, x-model, :accent — a framework directive rather than a prop of the component.
    private static bool IsDirective(string prop) =>
        prop.StartsWith("wire:", StringComparison.Ordinal) || prop.StartsWith("x-", StringComparison.Ordinal);

    // icon:trailing -> IconTrailing, tooltip:position -> TooltipPosition, 2xl -> 2xl is left to the enum's own spelling.
    private static string Pascal(string name) =>
        string.Concat(name.Split('-', ':', '.', ' ').Where(word => word.Length > 0).Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
}
