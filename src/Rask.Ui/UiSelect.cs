using System.Globalization;
using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask;

/// <summary>
/// A field with a fixed set of answers.
/// </summary>
/// <remarks>
/// <para>
/// A real <c>&lt;select&gt;</c> by default, and that is the version to reach for: it works with a
/// keyboard, a screen reader and a phone's native picker without a line of script, and it renders
/// complete on a prerendered page. <see cref="Native" /> set to <c>false</c> draws the list instead —
/// see the property for what that buys and what it costs.
/// </para>
/// <para>
/// Both modes are the same control. <see cref="Options" />, <c>Value</c>, <c>Bind</c>,
/// <c>OnChange</c>, <see cref="Placeholder" />, <c>Tone</c>, <c>Size</c> and
/// <c>Disabled</c> mean exactly the same thing either way; the flag chooses how the list is
/// DRAWN, not what the control is.
/// </para>
/// <para>
/// It is a form control: <c>.Bind(() =&gt; model.Country)</c> two-way binds and drives the surrounding
/// <c>Form</c>'s validation, or <c>Value</c> with <c>OnChange</c> lets the parent own
/// the value. The two are mutually exclusive at the call site — the generator emits a factory for each
/// and excludes the other mode's members from it.
/// </para>
/// </remarks>
public sealed partial class UiSelect<T> : UiFormField<T>
{
    // Per-instance, so two id-less selects on one page cannot collide on option ids —
    // aria-activedescendant points at them by id, and a collision aims it at the wrong list.
    private static int _instances;

    private readonly int _instance = Interlocked.Increment(ref _instances);

    private bool _open;
    private int _cursor = -1;
    private string? _filter;
    private UiTypeAhead _typeAhead;

    /// <summary>
    ///     The options: the value stored, and the words shown.
    /// </summary>
    /// <remarks>
    ///     Not the step that pins <typeparamref name="T" /> — the chain's OPENING does that, and for a
    ///     form control the opening is <c>Value</c> or <c>Bind</c>, which fix the type
    ///     and the mode together. So a call site reads
    ///     <c>Ui.Select.Value(x).Options(…).Label(…)</c>, and <c>Label</c>/<c>Options</c> may come in
    ///     either order after it.
    /// </remarks>
    public required IReadOnlyList<(T Value, string Text)> Options { get; set; }

    /// <summary>Shown first and unselectable — the prompt, not an answer.</summary>
    public string? Placeholder { get; set; }

    /// <summary>
    ///     Puts a search box at the top of the drawn list, so a long list is narrowed by typing — Flux UI's
    ///     searchable select.
    /// </summary>
    /// <remarks>
    ///     Matches an option's words, case- and accent-insensitively, unless <see cref="Filter" /> says what a
    ///     match is. Implies the drawn list: the platform's <c>&lt;select&gt;</c> has nowhere to put a search box.
    ///     The drawn list also takes TYPE-AHEAD without this — a letter jumps to the next option starting with it —
    ///     which is what a native select does and all a short list needs.
    /// </remarks>
    public bool? Searchable { get; set; }

    /// <summary>What counts as a match while searching: the option, and what was typed.</summary>
    /// <remarks>
    ///     For searching something other than the words shown — a country's code as well as its name, a person's
    ///     email. Implies <see cref="Searchable" />.
    /// </remarks>
    public Fn<T, string, bool>? Filter { get; set; }

    /// <summary>
    ///     Hands what was typed to the page instead of filtering here, for a list that comes from a server.
    /// </summary>
    /// <remarks>
    ///     With one, nothing is filtered locally: the page runs its own query and hands back new
    ///     <see cref="Options" />, showing <see cref="Loading" /> while it waits. Implies <see cref="Searchable" />.
    /// </remarks>
    public Callback<string> OnSearch { get; set; }

    /// <summary>Whether the options are still being fetched — shows "Searching…" in place of the list.</summary>
    public bool? Loading { get; set; }

    /// <summary>What the list says when nothing matches. "No results found" unless this says otherwise.</summary>
    public string? EmptyText { get; set; }

    /// <summary>What the list says while <see cref="Loading" />. "Searching…" unless this says otherwise.</summary>
    public string? LoadingText { get; set; }

    /// <summary>Adds a button that puts the field back to nothing chosen.</summary>
    /// <remarks>Only for a field that may legitimately be empty; it commits <c>default</c>, as the placeholder does.</remarks>
    public bool? Clearable { get; set; }

    /// <summary>
    ///     Draw the list here instead of handing it to the platform. Unset is the real
    ///     <c>&lt;select&gt;</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Turn it off when the option list has to carry more than the platform will show — a
    ///         grouped list, options that are visibly unavailable, a list that must escape an
    ///         <c>overflow: hidden</c> ancestor. The list is a <c>[popover]</c>, so the browser gives it
    ///         the top layer and dismisses it on Escape and on a click outside.
    ///     </para>
    ///     <para>
    ///         <b>It needs the runtime.</b> The custom list is inert on a prerendered page and does
    ///         nothing with scripting off, where the native control is completely working. That is the
    ///         cost of leaving the platform's own control, and it is why this defaults to native.
    ///     </para>
    ///     <para>
    ///         Its placement uses CSS anchor positioning, which not every engine ships yet; where it is
    ///         missing the list still opens and is still usable, centred rather than under its box. The
    ///         same trade <see cref="UiMegamenu" /> already makes.
    ///     </para>
    /// </remarks>
    public bool? Native { get; set; }

    /// <summary>
    ///     Marks options unselectable. The keyboard cursor skips them rather than landing on one.
    /// </summary>
    /// <remarks>Non-native only — a native <c>&lt;select&gt;</c> disables its options itself.</remarks>
    public Fn<T, bool>? OptionDisabled { get; set; }

    /// <summary>Buckets options under headers, in first-seen order.</summary>
    /// <remarks>
    ///     Grouping reorders the flat option list rather than nesting it, so the arrow keys still move
    ///     to the next option a reader can SEE.
    /// </remarks>
    public Fn<T, string>? OptionGroup { get; set; }

    /// <summary>Draws each option in the list, in place of its words.</summary>
    /// <remarks>
    ///     Setting it implies the drawn list, because an <c>&lt;option&gt;</c>'s content model is text:
    ///     there is nowhere in the platform's control for markup to go. Writing <c>Native(true)</c>
    ///     beside one is the contradiction, and RASK075 says so.
    ///     <para>
    ///         The <c>Text</c> from <see cref="Options" /> is still what the closed box shows, so it
    ///         stays worth supplying.
    ///     </para>
    /// </remarks>
    public Fn<T, Component>? OptionTemplate { get; set; }

    /// <summary>
    ///     Posts the value from a plain HTML form.
    /// </summary>
    /// <remarks>
    ///     Non-native only, and it renders a hidden input: a listbox built from buttons submits nothing
    ///     on its own, so without this a control inside a <c>&lt;form&gt;</c> would silently drop its
    ///     field. The native control posts under its own name and ignores this.
    /// </remarks>
    public string? Name { get; set; }

    private string Prefix => "uisel-" + _instance.ToString(CultureInfo.InvariantCulture);

    private string ListId => Prefix + "-list";

    // The popover element, which is the panel around the list rather than the list itself — see Custom.
    private string PanelId => Prefix + "-panel";

    // An OptionTemplate has nowhere to render inside an <option>, so supplying one chooses the drawn
    // list. An explicit Native(true) beside one is a contradiction rather than a preference, and RASK075
    // reports it at the call site — this is only what happens when nothing was said either way.
    private bool DrawsOwnList => Native is { } native
        ? !native
        // Every one of these needs somewhere to put markup the platform's control has no room for: templated
        // options, a search box, a clear button.
        : OptionTemplate is not null || Searchable == true || Filter is not null || OnSearch.HasValue
          || Clearable == true;

    private bool HasSearch => Searchable == true || Filter is not null || OnSearch.HasValue;

    private string SearchId => Prefix + "-search";

    /// <summary>
    ///     Whether a <c>Label</c> floats over the box rather than sitting above it as a legend. On unless this
    ///     is <see langword="false" />.
    /// </summary>
    /// <remarks>
    ///     Native only. daisyUI styles a floating label for a real <c>&lt;select&gt;</c>; the drawn list is a
    ///     button and a popover, which a floating caption does not know how to sit over, so it keeps the
    ///     legend whatever this says.
    /// </remarks>
    public bool? Floating { get; set; }

    /// <inheritdoc />
    private protected override bool FloatsLabel => Floating != false && !DrawsOwnList;

    // The clock the type-ahead's prefix expires by. Internal, so it is not a chain step; a test hands it its own.
    internal TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <inheritdoc />
    /// <inheritdoc />
    protected override Component Control() => DrawsOwnList ? Custom() : NativeSelect();

    // The platform's control. Everything form-shaped is forwarded to Rask.Core's Select<T>, which is
    // itself an IFormControl<T> — so binding, validation registration and the change parse are the
    // framework's here rather than reimplemented, and there is exactly one place they can drift from.
    private Component NativeSelect()
    {
        // Bound and controlled are different CHAIN TYPES, not two settings on one — Bind and Value are
        // mutually exclusive openings, so each mode is built as its own complete expression rather than
        // by mutating a shared variable.
        var current = Current();
        var options = OptionRows(current);

        if (Bind is { } bind)
        {
            return Select
                .Bind(bind)
                .Id(FieldId)
                .Validate(Validate)
                .AfterBind(AfterBind)
                .Aria(Aria(expanded: null))
                .Disabled(Disabled == true)
                .Class(BoxClass())[options];
        }

        return Select
            .Value(Value)
            .Id(FieldId)
            .OnChange(OnChange)
            .Aria(Aria(expanded: null))
            .Disabled(Disabled == true)
            .Class(BoxClass())[options];
    }

    private IEnumerable<Component?> OptionRows(T? current)
    {
        // `disabled` as well as empty: a placeholder that can be chosen is an answer, and one chosen by
        // accident is a bug report about a form that saved nothing.
        if (Placeholder is { } placeholder)
        {
            yield return Option.Value(string.Empty).Disabled(true).Selected(current is null)[placeholder];
        }

        foreach (var (value, text) in Options)
        {
            yield return Option
                .Key(OptionText(value))
                .Value(OptionText(value))
                .Selected(Same(value, current))[text];
        }
    }

    // The drawn list. A <button role="combobox"> naming a [popover] <ul role="listbox"> — the browser
    // owns dismissal, C# owns the state, and the two are kept in step by the popover's toggle event.
    private Component Custom()
    {
        var acc = Bind is { } bind ? ExpressionAccessor.Parse(bind) : null;
        var ctx = acc is null ? null : BindingHelpers.ResolveBindingContext(acc.Target);
        if (acc is not null)
        {
            // Every render, deliberately: passing the collapsed validator each time is also what clears
            // a stale rule when the consumer stops supplying one.
            ((IFormControl<T>)this).RegisterValidator(acc, ctx);
        }

        var current = acc is not null ? acc.Getter() is T v ? v : default : Value;
        var layout = UiSelectNav.Build(
            Shown(),
            OptionGroup is { } g ? o => g.Invoke(o.Value) ?? string.Empty : null);
        var flat = layout.Flat;
        var disabled = Disabledness(flat);
        var cursor = UiSelectNav.Normalize(_cursor, flat.Count, disabled);

        // The id is what the legend's `for` names. Without it the label pointed at nothing, and the box
        // was named only by the aria-label this used to duplicate from it.
        var box = Button
            .Id(FieldId)
            .Type("button")
            .Role("combobox")
            .Class(UiClass.Compose(BoxClass(), "flex items-center justify-between text-left"))
            .Disabled(Disabled == true)
            .Aria(Aria(expanded: _open, activeDescendant: _open && cursor >= 0
                ? UiSelectNav.OptId(Prefix, cursor)
                : null))
            .Attributes(
                // The POPOVER is the panel, not the list inside it — see below. aria-controls still
                // names the listbox, which is the thing a reader is being told about.
                ("popovertarget", PanelId),
                // The anchor the list positions against. A custom property in a style attribute, not a
                // class — nothing here is scanned by Tailwind, so building the name is safe.
                ("style", "anchor-name:--" + Prefix))
            // Deliberately does NOT touch _open. `popovertarget` means the browser is opening or
            // closing the list either way, and its toggle event below is what says which — so mirroring
            // the guess here gave the same field two writers. They race: both fire from one click, they
            // arrive over the socket in whichever order the frames land, and a click that arrived after
            // its own toggle flipped `aria-expanded` back to false over a list that was plainly open.
            // All this does is have a cursor ready for the frame that opens.
            .OnClick(() => _cursor = UiSelectNav.Seed(IndexOf(flat, current), flat.Count, disabled))
            .OnKeyDown(e => OnKeyAsync(e, acc, ctx, flat, disabled, current));

        // The popover is a PANEL around the list, and the extra element is load-bearing twice over.
        //
        // The browser hides a closed popover with a UA rule, `[popover]:not(:popover-open) { display:
        // none }`, and an author rule beats the UA sheet whatever its specificity — so putting a class
        // that sets `display` on the popover element leaves it on screen while closed. daisyUI's `menu`
        // is exactly such a class. Keeping `menu` on the inner <ul> lets the panel keep the UA's own
        // display, which is the whole mechanism.
        //
        // It is also NOT `dropdown-content`, and the wrapper below is not `dropdown`. That pair is
        // daisyUI's CSS dropdown, which reveals itself on the wrapper's :focus-within — a list that
        // appears the instant the box takes focus appears OVER the box, so the mousedown that focused it
        // is followed by a click that lands on the list instead of the invoker and the popover never
        // opens at all. One of those rules also puts `pointer-events: none` on the wrapper's first
        // child. A control cannot be a CSS dropdown and a popover at once; this one is a popover.
        var panel = Div
            .Id(PanelId)
            .Popover("auto")
            .Class("z-1 max-h-64 overflow-y-auto rounded-box border border-base-300 bg-base-100 "
                + "p-2 shadow-sm")
            // Placement, which `dropdown-content` used to supply. `position-area` puts the panel under
            // its anchor and `anchor-size` matches the box's width; an engine that ships neither
            // ignores both and the popover keeps its own default, which is centred — the list still
            // opens and is still usable, and it is the same trade Ui.Megamenu already makes.
            .Attributes(("style", "position-anchor:--" + Prefix
                                  + ";position-area:block-end span-inline-end"
                                  + ";width:anchor-size(width);margin:0"))
            // The SOLE writer of _open, and that is the point rather than an implementation detail: the
            // browser owns whether a popover is open — it opens one from `popovertarget` and closes it
            // on Escape and on a click outside — so anything else keeping its own copy is a second
            // answer to a question with one. Hearing this is what keeps aria-expanded truthful.
            .OnToggle(e =>
            {
                _open = e.IsOpen;
                _cursor = _open
                    ? UiSelectNav.Seed(IndexOf(flat, current), flat.Count, disabled)
                    : -1;
                if (!_open)
                {
                    // A list reopened on yesterday's search shows a narrowed list nobody asked for.
                    _filter = null;
                }
            })[
            SearchBox(acc, ctx, flat, disabled, current, cursor),
            Ul
                .Id(ListId)
                .Role("listbox")
                .Class("menu w-full flex-nowrap p-0")
                // The list is a separate widget in the top layer and needs its own name. Omitted when there is
                // none to give: a null value renders a valueless aria-label.
                .Aria((Label ?? AccessibleLabel) is { } listName
                    ? new Dictionary<string, string?> { ["label"] = listName }
                    : [])[
                flat.Count == 0 || Loading == true
                    ? Li.Class("px-3 py-2 text-sm opacity-60")[
                        Loading == true ? LoadingText ?? "Searching…" : EmptyText ?? "No results found"
                    ]
                    : Rows(layout, acc, ctx, current, cursor)
            ]
        ];

        return Div.Class(UiClass.Compose("relative w-full", Class))[
            box[Span.Class("truncate")[Display(current)]],
            // Beside the box rather than inside it: a button cannot hold another button, and the box is one.
            Clearable == true && current is not null && Disabled != true
                ? Button
                    .Type("button")
                    .Class("absolute inset-y-0 end-7 my-auto flex size-5 items-center justify-center rounded "
                           + "opacity-60 hover:opacity-100")
                    .Aria("label", "Clear " + (Label ?? AccessibleLabel ?? "selection"))
                    .OnClick(() => CommitAsync(acc, ctx, default!))[
                    Ui.Icon.Name(Ui.IconName.Close).Class("size-4")
                ]
                : null,
            panel,
            // A listbox of buttons submits nothing. Without this a control inside a plain <form> would
            // silently drop its field, which is the kind of failure nobody sees until the data is wrong.
            Name is { } name
                ? Input.Value(current is null ? string.Empty : OptionText(current)).Type(InputType.Hidden).Name(name)
                : null
        ];
    }

    // The options to draw: every one of them, unless a search box narrowed them here. With OnSearch the page runs
    // the query, so what it handed back IS the answer and filtering it again would narrow it twice.
    private IReadOnlyList<(T Value, string Text)> Shown()
    {
        if (!HasSearch || OnSearch.HasValue || string.IsNullOrEmpty(_filter))
        {
            return Options;
        }

        var needle = _filter;
        var shown = new List<(T Value, string Text)>();
        foreach (var option in Options)
        {
            var hit = Filter is { } match
                ? match.Invoke(option.Value, needle) == true
                // The visitor's culture, and ignoring case and accents: what counts as a match for "ö" is a local
                // question, and a reader typing "o" means to find "Ö".
                : CultureInfo.CurrentCulture.CompareInfo.IndexOf(
                    option.Text, needle, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
            if (hit)
            {
                shown.Add(option);
            }
        }

        return shown;
    }

    private IEnumerable<Component?> Rows(
        UiSelectNav.Layout<(T Value, string Text)> layout,
        ExpressionAccessor.Accessor? acc,
        EditContext? ctx,
        T? current,
        int cursor)
    {
        foreach (var group in layout.Groups)
        {
            if (group.Header is { } header)
            {
                yield return Li.Key("g-" + header).Class("menu-title")[header];
            }

            foreach (var row in group.Rows)
            {
                yield return Row(row, acc, ctx, current, cursor);
            }
        }
    }

    private Component Row(
        UiSelectNav.FlatRow<(T Value, string Text)> row,
        ExpressionAccessor.Accessor? acc,
        EditContext? ctx,
        T? current,
        int cursor)
    {
        var (value, text) = row.Item;
        var off = OptionDisabled?.Invoke(value) == true;
        var selected = Same(value, current);

        var option = Button
            .Type("button")
            .Role("option")
            .Id(UiSelectNav.OptId(Prefix, row.FlatIndex))
            .Class(UiClass.Compose(
                // menu-active is the SELECTED option; menu-focus is where the keyboard cursor sits.
                // They are different things and daisyUI draws them differently — the cursor can be on an
                // option that is not the selected one, which is the whole point of a roving cursor.
                selected ? "menu-active" : "",
                cursor == row.FlatIndex ? "menu-focus" : ""))
            .Disabled(off)
            // aria-disabled is OMITTED when the option is enabled, never nulled. A null renders the
            // attribute valueless, and a valueless aria-disabled reads as "true" — so the tidy
            // conditional value would have made every selectable option announce itself unavailable.
            .Aria(off
                ? new Dictionary<string, string?> { ["selected"] = "false", ["disabled"] = "true" }
                : new Dictionary<string, string?> { ["selected"] = selected ? "true" : "false" })
            // Closes the list declaratively as well as through the callback, so the dismissal does not
            // depend on the runtime having attached anything.
            .Attributes(("popovertarget", PanelId), ("popovertargetaction", "hide"));

        if (!off)
        {
            option = option.OnClick(() => CommitAsync(acc, ctx, value));
        }

        // menu-disabled goes on the <li>, unlike menu-active and menu-focus, which go on the child.
        return Li.Key(row.FlatIndex).Class(off ? "menu-disabled" : "")[
            option[OptionTemplate is { } template && template.Invoke(value) is { } drawn ? drawn : text]
        ];
    }

    // Combobox keyboard over the flat option list: arrows move the cursor (skipping disabled options),
    // Home/End jump to the first/last enabled one, Enter picks. Escape is left alone — the browser's
    // own dismissal is what closes the popover, and OnToggle above hears about it.
    // The search box lives INSIDE the popover, above the list: it opens with the list, takes focus from the
    // popover's own focusing steps, and carries the cursor's ARIA — aria-activedescendant only announces the
    // option from the element that actually has focus.
    private Component? SearchBox(
        ExpressionAccessor.Accessor? acc,
        EditContext? ctx,
        IReadOnlyList<(T Value, string Text)> flat,
        Func<int, bool> disabled,
        T? current,
        int cursor)
    {
        if (!HasSearch || !_open)
        {
            return null;
        }

        var aria = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["label"] = "Search " + (Label ?? AccessibleLabel ?? "options"),
            ["controls"] = ListId,
        };
        if (cursor >= 0)
        {
            aria["activedescendant"] = UiSelectNav.OptId(Prefix, cursor);
        }

        return Div.Class("px-1 pb-2")[
            Input
                .Value(_filter ?? string.Empty)
                .Id(SearchId)
                .Type(InputType.Text)
                .Class("input input-sm w-full")
                .Placeholder("Search…")
                .Autocomplete("off")
                .Autofocus(true)
                .Role("combobox")
                .Aria(aria)
                .OnInput(async raw =>
                {
                    _filter = raw;
                    // Back to the top of the narrowed list, which Normalize snaps onto the first option a reader
                    // can actually land on.
                    _cursor = 0;
                    await OnSearch.Invoke(raw ?? string.Empty).ConfigureAwait(false);
                })
                .OnKeyDown(e => OnKeyAsync(e, acc, ctx, flat, disabled, current, fromSearch: true))
        ];
    }

    private async Task OnKeyAsync(
        KeyboardEventArgs e,
        ExpressionAccessor.Accessor? acc,
        EditContext? ctx,
        IReadOnlyList<(T Value, string Text)> flat,
        Func<int, bool> disabled,
        T? current,
        bool fromSearch = false)
    {
        var count = flat.Count;
        var cursor = UiSelectNav.Normalize(_cursor, count, disabled);

        if (!_open)
        {
            // Closed, the arrows move the SELECTION rather than opening the list — what a native select
            // does on a desktop, and what lets a reader change the answer without ever seeing the list.
            switch (e.Key)
            {
                case "ArrowDown":
                case "ArrowUp":
                    var next = UiSelectNav.Step(
                        IndexOf(flat, current), e.Key == "ArrowDown" ? 1 : -1, count, disabled);
                    if (next >= 0 && next < count)
                    {
                        await CommitAsync(acc, ctx, flat[next].Value).ConfigureAwait(false);
                    }

                    break;
            }

            return;
        }

        switch (e.Key)
        {
            case "ArrowDown":
                _cursor = UiSelectNav.Step(cursor, 1, count, disabled);
                break;
            case "ArrowUp":
                _cursor = UiSelectNav.Step(cursor, -1, count, disabled);
                break;
            case "Home":
                _cursor = UiSelectNav.FirstEnabled(count, disabled);
                break;
            case "End":
                _cursor = UiSelectNav.LastEnabled(count, disabled);
                break;
            case "Enter":
                if (cursor >= 0 && cursor < count && !disabled(cursor))
                {
                    await CommitAsync(acc, ctx, flat[cursor].Value).ConfigureAwait(false);
                }

                break;
            default:
                // Type-ahead, the way a native select answers a letter — but only with focus on the BOX. In the
                // search field the same keystroke is what is being searched for.
                if (!fromSearch && e.Key.Length == 1 && e.Key != " " && !e.Ctrl && !e.Alt && !e.Meta)
                {
                    var texts = new string?[count];
                    for (var i = 0; i < count; i++)
                    {
                        texts[i] = disabled(i) ? null : flat[i].Text;
                    }

                    var hit = _typeAhead.Next(e.Key, cursor, texts, Clock);
                    if (hit >= 0)
                    {
                        _cursor = hit;
                    }
                }

                break;
        }
    }

    // Writes the chosen value back to the model (bound) or notifies the parent (controlled), then
    // closes. No StateHasChanged: Rask re-renders the callback's owner already (RASK026).
    //
    // The browser closes the list here too — a chosen option carries `popovertargetaction="hide"`, and
    // committing from the keyboard means Enter on the focused box, which fires the box's own click and
    // toggles it shut. So this is a second writer of _open like the click handler was, and unlike that
    // one it cannot disagree: both say closed, whichever order the frames land in.
    private async Task CommitAsync(ExpressionAccessor.Accessor? acc, EditContext? ctx, T value)
    {
        _open = false;
        _cursor = -1;

        var self = (IFormControl<T>)this;
        if (acc is not null)
        {
            acc.Setter(value);
            await BindingHelpers.NotifyAndValidateFieldAsync(ctx, acc.Field).ConfigureAwait(false);
            await self.InvokeAfterBindAsync(value).ConfigureAwait(false);
        }
        else
        {
            await self.InvokeOnChangeAsync(value).ConfigureAwait(false);
        }
    }

    private Func<int, bool> Disabledness(IReadOnlyList<(T Value, string Text)> flat) =>
        OptionDisabled is { } off ? i => off.Invoke(flat[i].Value) : _ => false;

    private int IndexOf(IReadOnlyList<(T Value, string Text)> flat, T? current)
    {
        for (var i = 0; i < flat.Count; i++)
        {
            if (Same(flat[i].Value, current))
            {
                return i;
            }
        }

        return -1;
    }

    private string Display(T? current)
    {
        foreach (var (value, text) in Options)
        {
            if (Same(value, current))
            {
                return text;
            }
        }

        return Placeholder ?? "";
    }

    private T? Current() =>
        Bind is { } bind && ExpressionAccessor.Parse(bind).Getter() is T v ? v : Value;

    private string BoxClass() =>
        UiClass.Compose(
            "select validator",
            Tone is { } tone ? UiClassNames.SelectTone(tone) : "",
            Size is { } size ? UiClassNames.SelectSize(size) : "",
            Variant is { } variant ? UiClassNames.SelectVariant(variant) : "",
            DrawsOwnList ? "" : Class);

    // The field's own name, invalid state and description first — the base resolves them once for every
    // control. This used to copy the invalid rule and write `aria-label` from Label: a second name beside the
    // visible label, and a VALUELESS aria-label on a select with no label at all.
    private Dictionary<string, string?> Aria(bool? expanded, string? activeDescendant = null)
    {
        var aria = ControlAria();

        if (expanded is { } open)
        {
            aria["haspopup"] = "listbox";
            aria["expanded"] = open ? "true" : "false";
            aria["controls"] = ListId;
        }

        if (activeDescendant is { } active)
        {
            aria["activedescendant"] = active;
        }

        return aria;
    }

    private static bool Same(T? a, T? b) => EqualityComparer<T?>.Default.Equals(a, b);

    private static string OptionText(T value) => BindingHelpers.FormatValue(value);
}
