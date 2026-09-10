using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Ui;

/// <summary>
/// A field with a fixed set of answers, where more than one may be chosen.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="UiSelect{T}" />'s sibling, and deliberately a separate control rather than a mode of it:
/// <c>IFormControl&lt;T&gt;</c> is keyed on one <c>T</c>, and a control that bound both one answer and
/// several would have two sources of truth for one field. Everything else is shared — the same prop
/// names, the same drawn listbox, the same popover, the same keyboard.
/// </para>
/// <para>
/// A real <c>&lt;select multiple&gt;</c> by default, which works with a keyboard and a screen reader,
/// renders complete on a prerendered page and posts under its own name with no script at all.
/// <see cref="Native" /> set to <c>false</c> draws the list instead, with the chosen answers as
/// removable chips in the box — and an <see cref="OptionTemplate" /> implies it, since an
/// <c>&lt;option&gt;</c> holds text and nothing else.
/// </para>
/// <para>
/// It binds the collection your model already declares. <c>.Bind(() =&gt; model.Tags)</c> two-way binds a
/// <c>List&lt;T&gt;</c>, a <c>T[]</c> or a <c>HashSet&lt;T&gt;</c> — the write-back builds whatever the
/// property declares, and refills a get-only collection in place. A field typed
/// <c>IReadOnlyList&lt;T&gt;</c> is the one shape that cannot bind: it is not an
/// <c>ICollection&lt;T&gt;</c>, so the chain has nothing to infer from.
/// </para>
/// </remarks>
public sealed partial class UiMultiSelect<T> : Component, IFormControl<ICollection<T>>
{
    // How many chips the box shows before it collapses the rest into "+N more". Chips(0) turns them off
    // entirely and leaves the count on its own.
    private const int DefaultChips = 3;

    // Per-instance, so two id-less controls on one page cannot collide on option ids — see UiSelect.
    private static int _instances;

    private readonly int _instance = Interlocked.Increment(ref _instances);

    private bool _open;
    private int _cursor = -1;
    private string? _filter;

    /// <summary>The accessible name.</summary>
    public required string Label { get; set; }

    /// <summary>The options: the values stored, and the words shown.</summary>
    /// <remarks>
    ///     Not the step that pins <typeparamref name="T" /> — the chain's OPENING does that, and for a
    ///     form control the opening is <see cref="Value" /> or <see cref="Bind" />, which fix the type
    ///     and the mode together.
    /// </remarks>
    public required IReadOnlyList<(T Value, string Text)> Options { get; set; }

    /// <summary>Shown in the box while nothing is chosen.</summary>
    /// <remarks>
    ///     Unlike <see cref="UiSelect{T}" />'s, this never becomes an option. A "choose one" row in a
    ///     list you may choose several from is an answer that contradicts the question.
    /// </remarks>
    public string? Placeholder { get; set; }

    /// <summary>
    ///     Draw the list here instead of handing it to the platform. Unset is the real
    ///     <c>&lt;select multiple&gt;</c> — unless an <see cref="OptionTemplate" /> is set, which implies
    ///     the drawn list.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Turn it off for the chips, the search box, the select-all, or an option list that has to
    ///         carry more than the platform will show. The drawn list is also the answer to the native
    ///         multi-select's real problem, which is that adding a second answer means knowing to hold
    ///         ctrl.
    ///     </para>
    ///     <para>
    ///         <b>It needs the runtime.</b> The drawn list is inert on a prerendered page and does
    ///         nothing with scripting off, where the native control is completely working.
    ///     </para>
    /// </remarks>
    public bool? Native { get; set; }

    /// <summary>Marks options unselectable. The keyboard cursor skips them rather than landing on one.</summary>
    /// <remarks>Non-native only — a native <c>&lt;select&gt;</c> disables its options itself.</remarks>
    public Fn<T, bool>? OptionDisabled { get; set; }

    /// <summary>Buckets options under headers, in first-seen order.</summary>
    public Fn<T, string>? OptionGroup { get; set; }

    /// <summary>Draws each option in the list, in place of its words.</summary>
    /// <remarks>
    ///     Setting it implies the drawn list, because an <c>&lt;option&gt;</c>'s content model is text:
    ///     there is nowhere in the platform's control for markup to go. Writing <c>Native(true)</c>
    ///     beside one is the contradiction, and RASK075 says so.
    ///     <para>
    ///         The <c>Text</c> from <see cref="Options" /> is still what the chips and the native control
    ///         show, so it stays worth supplying.
    ///     </para>
    /// </remarks>
    public Fn<T, Component>? OptionTemplate { get; set; }

    /// <summary>Draws each chosen answer as a chip, in place of its words.</summary>
    /// <remarks>A chip is a small pill in a fixed-height box; markup that does not fit will wrap it.</remarks>
    public Fn<T, Component>? ChipTemplate { get; set; }

    /// <summary>Narrows the list from a search box, by deciding what the typed text matches.</summary>
    /// <remarks>
    ///     Supplying it is what adds the box: you own the match, so it works for any
    ///     <typeparamref name="T" /> without this control assuming the shape of your data — e.g.
    ///     <c>(v, text) =&gt; v.Name.Contains(text, StringComparison.OrdinalIgnoreCase)</c>. Non-native
    ///     only.
    /// </remarks>
    public Fn<T, string, bool>? Filter { get; set; }

    /// <summary>Adds a row that chooses or clears every option at once.</summary>
    /// <remarks>
    ///     It applies to the options currently SHOWN and selectable, so with a search active it acts on
    ///     what the search left, and it never touches a disabled option. Non-native only.
    /// </remarks>
    public bool? SelectAll { get; set; }

    /// <summary>How many chips the box shows before the rest collapse into "+N more". Three by default.</summary>
    /// <remarks>
    ///     <c>0</c> turns the chips off and leaves the count alone, for a box that must not change width.
    ///     Non-native only — the platform's control draws its own selection.
    /// </remarks>
    public int? Chips { get; set; }

    /// <summary>
    ///     Posts the values from a plain HTML form.
    /// </summary>
    /// <remarks>
    ///     Non-native only, and it renders one hidden input per chosen answer, all sharing the name —
    ///     byte for byte what a <c>&lt;select multiple&gt;</c> posts, so a server that already reads the
    ///     native control needs no change. The native control posts under its own name and ignores this.
    /// </remarks>
    public string? Name { get; set; }

    public UiTone? Tone { get; set; }

    public UiSize? Size { get; set; }

    public UiVariant? Variant { get; set; }

    public bool? Disabled { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    public ICollection<T>? Value { get; set; }

    /// <inheritdoc />
    public Callback<ICollection<T>>? OnChange { get; set; }

    /// <inheritdoc />
    public Expression<Func<ICollection<T>>>? Bind { get; set; }

    /// <inheritdoc />
    public Validator<ICollection<T>>? Validate { get; set; }

    /// <inheritdoc />
    public Callback<ICollection<T>>? AfterBind { get; set; }

    // The selection, split into the part this control can draw and the part it cannot.
    //
    // A bound field may hold a value that is not in Options — an option list narrowed by permissions, a
    // tag retired since the row was saved. Those values have no row and no chip, so the control must
    // neither show them nor DESTROY them: every commit is rebuilt from the visible answers, and without
    // carrying the rest along, toggling one option would silently delete a value the user never touched
    // and could not see. The rule is one line — this control never adds or removes a value it cannot
    // draw — and Unseen is what keeps it.
    private readonly record struct Picked(IReadOnlyList<T> Shown, IReadOnlyList<T> Unseen)
    {
        public int Count => Shown.Count;

        public T this[int index] => Shown[index];
    }

    private string Prefix => "uims-" + _instance.ToString(CultureInfo.InvariantCulture);

    private string ListId => Prefix + "-list";

    private string PanelId => Prefix + "-panel";

    // An OptionTemplate has nowhere to render inside an <option>, so supplying one chooses the drawn
    // list. An explicit Native(true) beside one is a contradiction rather than a preference, and RASK075
    // reports it at the call site — this is only what happens when nothing was said either way.
    private bool DrawsOwnList => Native is { } native ? !native : OptionTemplate is not null;

    /// <inheritdoc />
    protected override Component? Render() => DrawsOwnList ? Custom() : NativeSelect();

    // The platform's control. Unlike UiSelect's native path this does NOT forward Bind to Select<T>:
    // Select's own multi-select binding runs through BindingHelpers.IsBindableSelectionType, whose
    // element type is closed to `string` for an AOT reason it documents. Taking the raw picked values
    // through OnSelect and mapping them back through this control's own Options costs one dictionary-free
    // scan and works for any T — an int, an enum, a Guid.
    private Component NativeSelect()
    {
        var (acc, ctx, current) = UiFormCommit.Resolve<ICollection<T>>(this);
        var chosen = Chosen(current);

        return Select
            .Of<string>()
            .Multiple(true)
            .Name(Name)
            .OnSelect(picked => CommitAsync(acc, ctx, chosen, Map(picked)))
            .Aria(Aria(expanded: null))
            .Disabled(Disabled == true)
            .Class(BoxClass())[
            Options.Select(o => Option
                .Key(OptionText(o.Value))
                .Value(OptionText(o.Value))
                .Disabled(OptionDisabled?.Invoke(o.Value) == true)
                .Selected(Contains(chosen.Shown, o.Value))[o.Text])
        ];
    }

    // The drawn list: a [popover] listbox under a box that holds the chosen answers as chips.
    //
    // The box is a <div role="combobox"> wrapping a <button>, where UiSelect's box IS the button. That is
    // not a style choice — a chip's remove control is a button, and a <button> may not contain a
    // <button>: interactive content is excluded from its content model. Nesting them is what the deleted
    // BsMultiSelect did with a real <input> inside its option rows, and it is invalid markup either way.
    // Keeping the popover's invoker as its own button preserves the declarative `popovertarget`, so the
    // browser still owns opening, Escape and click-outside.
    private Component Custom()
    {
        var (acc, ctx, current) = UiFormCommit.Resolve<ICollection<T>>(this);
        var chosen = Chosen(current);
        var disabled = Disabled == true;

        // Filtering narrows the option list BEFORE the layout is built, so the flat cursor space and the
        // rendered rows are the same list — which is what lets an arrow key follow the eye after a search.
        var shown = Filter is { } match && !string.IsNullOrEmpty(_filter)
            ? Options.Where(o => match.Invoke(o.Value, _filter) == true).ToArray()
            : Options;
        var layout = UiSelectNav.Build(shown, OptionGroup is { } g ? o => g.Invoke(o.Value) ?? string.Empty : null);
        var flat = layout.Flat;
        var off = Disabledness(flat);
        var cursor = UiSelectNav.Normalize(_cursor, flat.Count, off);

        var invoker = Button
            .Type("button")
            // min-h-6 is load-bearing, not spacing. This button says nothing at all whenever every
            // answer fitted into chips — Summary returns "" — and an empty flex child collapses to zero
            // height, which leaves the control with no region to click to open the list and a combobox
            // that assistive tooling and Playwright alike report as not visible. The caret beside it is
            // daisyUI's `.select` background image, painted on the box rather than on this button, so it
            // is no help: it looks clickable and is not. Only a browser catches this, and one did.
            .Class("flex min-h-6 flex-1 items-center justify-between gap-2 text-left")
            .Role("combobox")
            .Disabled(disabled)
            .Aria(Aria(expanded: _open, activeDescendant: _open && cursor >= 0
                ? UiSelectNav.OptId(Prefix, cursor)
                : null))
            .Attributes(("popovertarget", PanelId))
            // Deliberately does NOT touch _open — see the toggle handler below, which is its sole writer.
            // All this does is have a cursor ready for the frame that opens.
            .OnClick(() => _cursor = UiSelectNav.Seed(FirstChosen(flat, chosen), flat.Count, off))
            .OnKeyDown(e => OnKeyAsync(e, acc, ctx, flat, off, chosen, fromSearch: false))[
            Span.Class("truncate")[Summary(chosen)]
        ];

        // Presentational, and deliberately so: no role and no aria of its own. The combobox is the
        // button inside it — a second role="combobox" here would announce two controls where there is
        // one, and an aria-hidden meant to suppress that would bury the chips' remove buttons, which are
        // focusable. Focusable content inside aria-hidden is the one thing that rule must never do.
        var box = Div
            .Class(UiClass.Compose(BoxClass(), "flex h-auto min-h-12 flex-wrap items-center gap-1 py-1.5"))
            // The anchor the panel positions against is the whole BOX, not the invoker inside it, so the
            // list lines up with the control the reader sees rather than with part of it. A custom
            // property in a style attribute, not a class — nothing here is scanned by Tailwind.
            .Attributes(("style", "anchor-name:--" + Prefix))[
            ChipRow(acc, ctx, chosen, disabled),
            invoker
        ];

        return Div.Class(UiClass.Compose("w-full", Class))[
            box,
            Panel(acc, ctx, layout, flat, off, chosen, cursor, disabled),
            // A listbox of buttons submits nothing. One hidden input per answer, all sharing the name, is
            // exactly what <select multiple> posts — so a server reading the native control reads this one
            // unchanged, and nothing has to know which mode drew it.
            //
            // Unseen answers post too. They are part of the field's value, and a plain form has no model
            // to carry them separately — leaving them out would drop on submit exactly what the commit
            // path takes care to keep, which is the same data loss arriving by the other road.
            Name is { } name
                ? chosen.Shown.Concat(chosen.Unseen).Select(v => Input
                    .Value(OptionText(v))
                    .Key("h-" + OptionText(v))
                    .Type(InputType.Hidden)
                    .Name(name))
                : null
        ];
    }

    // The chips, and the control that clears them all. Empty (not a placeholder span) when nothing is
    // chosen: the invoker beside it already shows the placeholder, and two of them would be two answers.
    private IEnumerable<Component?> ChipRow(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, Picked chosen, bool disabled)
    {
        var limit = Chips ?? DefaultChips;
        for (var i = 0; i < chosen.Count && i < limit; i++)
        {
            var value = chosen[i];
            yield return Span
                .Key("c-" + OptionText(value))
                .Class("badge badge-sm badge-neutral gap-1")[
                ChipTemplate is { } template && template.Invoke(value) is { } chip ? chip : TextOf(value),
                disabled
                    ? null
                    : Button
                        .Type("button")
                        .Class("cursor-pointer opacity-70 hover:opacity-100")
                        .Aria(new Dictionary<string, string?> { ["label"] = "Remove " + TextOf(value) })
                        .OnClick(() => CommitAsync(acc, ctx, chosen, Without(chosen.Shown, value)))[
                        UiIcon.Name(UiIconName.Close).Class("size-3")
                    ]
            ];
        }

        if (chosen.Count > 0 && !disabled)
        {
            yield return Button
                .Type("button")
                .Class("cursor-pointer opacity-60 hover:opacity-100")
                .Aria(new Dictionary<string, string?> { ["label"] = "Clear all" })
                // Clears the answers this control drew, and only those.
                .OnClick(() => CommitAsync(acc, ctx, chosen, []))[
                UiIcon.Name(UiIconName.Close).Class("size-4")
            ];
        }
    }

    // The popover panel. Identical in mechanism to UiSelect's — the panel is a wrapper around the list
    // rather than the list itself, because an author rule setting `display` beats the UA's
    // `[popover]:not(:popover-open) { display: none }` and would leave a closed list on screen. daisyUI's
    // `menu` is such a class, so it stays on the inner <ul>.
    private Component Panel(
        ExpressionAccessor.Accessor? acc,
        EditContext? ctx,
        UiSelectNav.Layout<(T Value, string Text)> layout,
        IReadOnlyList<(T Value, string Text)> flat,
        Func<int, bool> off,
        Picked chosen,
        int cursor,
        bool disabled) =>
        Div
            .Id(PanelId)
            .Popover("auto")
            .Class("z-1 max-h-64 overflow-y-auto rounded-box border border-base-300 bg-base-100 "
                + "p-2 shadow-sm")
            .Attributes(("style", "position-anchor:--" + Prefix
                                  + ";position-area:block-end span-inline-end"
                                  + ";width:anchor-size(width);margin:0"))
            // The SOLE writer of _open. The browser owns whether a popover is open — it opens one from
            // `popovertarget` and closes it on Escape and on a click outside — so anything else keeping
            // its own copy is a second answer to a question with one, and the two race over the socket.
            .OnToggle(e =>
            {
                _open = e.IsOpen;
                _cursor = _open ? UiSelectNav.Seed(FirstChosen(flat, chosen), flat.Count, off) : -1;
                if (!_open)
                {
                    _filter = null;
                }
            })[
            Search(acc, ctx, flat, off, chosen),
            SelectAllRow(acc, ctx, flat, off, chosen, disabled),
            Ul
                .Id(ListId)
                .Role("listbox")
                .Class("menu w-full flex-nowrap p-0")
                .Aria(new Dictionary<string, string?>
                {
                    ["label"] = Label,
                    // What actually announces "you may pick several". Without it a reader meets a listbox
                    // whose options each say aria-selected and has no way to know a second one is allowed.
                    ["multiselectable"] = "true"
                })[
                flat.Count == 0
                    ? Li.Class("menu-disabled")[Span["No matches"]]
                    : Rows(layout, acc, ctx, chosen, cursor)
            ]
        ];

    // The search box, only when a Filter was supplied and only while open — rendering it closed would
    // autofocus a field inside a hidden popover.
    private Component? Search(
        ExpressionAccessor.Accessor? acc,
        EditContext? ctx,
        IReadOnlyList<(T Value, string Text)> flat,
        Func<int, bool> off,
        Picked chosen)
    {
        if (Filter is null || !_open)
        {
            return null;
        }

        return Div.Class("px-1 pb-2")[
            Input
                .Value(_filter ?? string.Empty)
                .Type(InputType.Text)
                .Class("input input-sm w-full")
                .Placeholder("Search…")
                .Autocomplete("off")
                .Autofocus(true)
                .Aria(new Dictionary<string, string?> { ["label"] = "Search " + Label })
                // Back to the top of the narrowed list, which Normalize then snaps onto the first option
                // a reader can actually land on.
                .OnInput(raw =>
                {
                    _filter = raw;
                    _cursor = 0;
                })
                .OnKeyDown(e => OnKeyAsync(e, acc, ctx, flat, off, chosen, fromSearch: true))
        ];
    }

    // "Select all" over the options currently SHOWN and selectable, flipping to "Clear all" once they are
    // all in. A bulk action rather than an answer, so it is not a role="option" and the cursor skips it.
    private Component? SelectAllRow(
        ExpressionAccessor.Accessor? acc,
        EditContext? ctx,
        IReadOnlyList<(T Value, string Text)> flat,
        Func<int, bool> off,
        Picked chosen,
        bool disabled)
    {
        if (SelectAll is not true || disabled)
        {
            return null;
        }

        var selectable = new List<T>();
        for (var i = 0; i < flat.Count; i++)
        {
            if (!off(i))
            {
                selectable.Add(flat[i].Value);
            }
        }

        if (selectable.Count == 0)
        {
            return null;
        }

        var allIn = selectable.All(v => Contains(chosen.Shown, v));
        return Button
            .Type("button")
            .Class("btn btn-ghost btn-xs mb-1 w-full justify-start")
            .OnClick(() => CommitAsync(acc, ctx, chosen, allIn
                ? [.. chosen.Shown.Where(v => !Contains(selectable, v))]
                : Union(chosen.Shown, selectable)))[
            allIn ? "Clear all" : "Select all"
        ];
    }

    private IEnumerable<Component?> Rows(
        UiSelectNav.Layout<(T Value, string Text)> layout,
        ExpressionAccessor.Accessor? acc,
        EditContext? ctx,
        Picked chosen,
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
                yield return Row(row, acc, ctx, chosen, cursor);
            }
        }
    }

    private Component Row(
        UiSelectNav.FlatRow<(T Value, string Text)> row,
        ExpressionAccessor.Accessor? acc,
        EditContext? ctx,
        Picked chosen,
        int cursor)
    {
        var (value, text) = row.Item;
        var off = OptionDisabled?.Invoke(value) == true;
        var selected = Contains(chosen.Shown, value);

        var option = Button
            .Type("button")
            .Role("option")
            .Id(UiSelectNav.OptId(Prefix, row.FlatIndex))
            .Class(UiClass.Compose(
                // The single-select's vocabulary, unchanged: menu-active is a CHOSEN option, menu-focus is
                // where the keyboard cursor sits. Several rows are active here where at most one ever was,
                // which is the one thing this list asks a reader to hold that UiSelect's does not.
                selected ? "menu-active" : "",
                cursor == row.FlatIndex ? "menu-focus" : ""))
            .Disabled(off)
            // aria-disabled is OMITTED when the option is enabled, never nulled: a valueless aria-disabled
            // reads as "true", so the tidy conditional value would mark every option unavailable.
            .Aria(off
                ? new Dictionary<string, string?> { ["selected"] = "false", ["disabled"] = "true" }
                : new Dictionary<string, string?> { ["selected"] = selected ? "true" : "false" });

        // No popovertargetaction="hide", and that omission is the feature: picking one answer out of
        // several must not close the list, or choosing three means opening it three times.
        if (!off)
        {
            option = option.OnClick(() => CommitAsync(acc, ctx, chosen, Toggled(chosen.Shown, value)));
        }

        // menu-disabled goes on the <li>, unlike menu-active and menu-focus, which go on the child.
        return Li.Key(row.FlatIndex).Class(off ? "menu-disabled" : "")[
            option[OptionTemplate is { } template && template.Invoke(value) is { } drawn ? drawn : text]
        ];
    }

    // Combobox keyboard over the flat option list. Unlike the single-select's, the arrows do nothing
    // while closed: there is no "next" answer to move to when several may be held, so every key that
    // means business opens the list instead. Escape is left alone — the browser's own dismissal closes
    // the popover, and the toggle handler hears it.
    private async Task OnKeyAsync(
        KeyboardEventArgs e,
        ExpressionAccessor.Accessor? acc,
        EditContext? ctx,
        IReadOnlyList<(T Value, string Text)> flat,
        Func<int, bool> off,
        Picked chosen,
        bool fromSearch)
    {
        var count = flat.Count;
        var cursor = UiSelectNav.Normalize(_cursor, count, off);

        if (!_open)
        {
            if (e.Key is "ArrowDown" or "ArrowUp" or "Enter" or " ")
            {
                _cursor = UiSelectNav.Seed(FirstChosen(flat, chosen), count, off);
            }

            return;
        }

        switch (e.Key)
        {
            case "ArrowDown":
                _cursor = UiSelectNav.Step(cursor, 1, count, off);
                break;
            case "ArrowUp":
                _cursor = UiSelectNav.Step(cursor, -1, count, off);
                break;
            case "Home":
                _cursor = UiSelectNav.FirstEnabled(count, off);
                break;
            case "End":
                _cursor = UiSelectNav.LastEnabled(count, off);
                break;
            case "Enter":
            // In the search field Space types a space. Toggling on it there would make the box refuse
            // the one character a multi-word search needs most.
            case " " when !fromSearch:
                if (cursor >= 0 && cursor < count && !off(cursor))
                {
                    await CommitAsync(acc, ctx, chosen, Toggled(chosen.Shown, flat[cursor].Value))
                        .ConfigureAwait(false);
                }

                break;
        }
    }

    // Writes the whole selection back, and leaves the list open. No StateHasChanged: Rask re-renders the
    // callback's owner already (RASK026).
    //
    // `shown` is the new visible selection; whatever the model held that this control cannot draw rides
    // along untouched, so a commit never destroys a value the user could not see.
    private Task CommitAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, Picked chosen, IReadOnlyList<T> shown) =>
        UiFormCommit.CommitSelectionAsync(this, acc, ctx, [.. shown, .. chosen.Unseen]);

    // The picked option values, mapped back through this control's own option list. Never parsed: the
    // strings that arrive are ones this control rendered, so matching them against the same formatting is
    // exact and needs no IParsable, no enum lookup and no reflection.
    private IReadOnlyList<T> Map(IReadOnlyList<string> picked)
    {
        var wanted = new HashSet<string>(picked, StringComparer.Ordinal);
        var mapped = new List<T>(picked.Count);
        foreach (var (value, _) in Options)
        {
            if (wanted.Contains(OptionText(value)))
            {
                mapped.Add(value);
            }
        }

        return mapped;
    }

    // The current selection, in the OPTIONS' order rather than the order the model happens to hold them:
    // the chips and the "+N more" cut then match the list the reader just used. Anything the model holds
    // that has no option goes to Unseen rather than being dropped — see Picked.
    private Picked Chosen(ICollection<T>? current)
    {
        if (current is null || current.Count == 0)
        {
            return new Picked([], []);
        }

        var shown = new List<T>(current.Count);
        foreach (var (value, _) in Options)
        {
            if (Contains(current, value))
            {
                shown.Add(value);
            }
        }

        var unseen = new List<T>();
        foreach (var value in current)
        {
            if (!Contains(shown, value))
            {
                unseen.Add(value);
            }
        }

        return new Picked(shown, unseen);
    }

    // What the invoker says: the answers that did not fit as chips, or the placeholder. With Chips(0) no
    // chip was drawn at all, so the count stands alone and the box keeps one width whatever is picked.
    private string Summary(Picked chosen)
    {
        if (chosen.Count == 0)
        {
            return Placeholder ?? "";
        }

        var limit = Chips ?? DefaultChips;
        if (limit == 0)
        {
            return chosen.Count.ToString(CultureInfo.InvariantCulture) + " selected";
        }

        var hidden = chosen.Count - limit;
        return hidden > 0 ? "+" + hidden.ToString(CultureInfo.InvariantCulture) + " more" : "";
    }

    private string TextOf(T value)
    {
        foreach (var (candidate, text) in Options)
        {
            if (Same(candidate, value))
            {
                return text;
            }
        }

        return OptionText(value);
    }

    private Func<int, bool> Disabledness(IReadOnlyList<(T Value, string Text)> flat) =>
        OptionDisabled is { } off ? i => off.Invoke(flat[i].Value) == true : _ => false;

    private int FirstChosen(IReadOnlyList<(T Value, string Text)> flat, Picked chosen)
    {
        for (var i = 0; i < flat.Count; i++)
        {
            if (Contains(chosen.Shown, flat[i].Value))
            {
                return i;
            }
        }

        return -1;
    }

    private string BoxClass() =>
        UiClass.Compose(
            "select validator",
            Tone is { } tone ? UiClassNames.SelectTone(tone) : "",
            Size is { } size ? UiClassNames.SelectSize(size) : "",
            Variant is { } variant ? UiClassNames.SelectVariant(variant) : "",
            DrawsOwnList ? "" : Class);

    // aria-invalid is what makes daisyUI reveal a following UiValidator, and what a screen reader needs.
    // OMITTED rather than nulled — a valueless aria-invalid reads as "true".
    private Dictionary<string, string?> Aria(bool? expanded, string? activeDescendant = null)
    {
        var aria = new Dictionary<string, string?> { ["label"] = Label };
        if (Tone == UiTone.Error)
        {
            aria["invalid"] = "true";
        }

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

    private static IReadOnlyList<T> Toggled(IReadOnlyList<T> chosen, T value) =>
        Contains(chosen, value) ? Without(chosen, value) : [.. chosen, value];

    private static IReadOnlyList<T> Without(IReadOnlyList<T> chosen, T value) =>
        [.. chosen.Where(v => !Same(v, value))];

    private static IReadOnlyList<T> Union(IReadOnlyList<T> chosen, IReadOnlyList<T> adding) =>
        [.. chosen, .. adding.Where(v => !Contains(chosen, v))];

    private static bool Contains(IEnumerable<T> values, T value)
    {
        foreach (var candidate in values)
        {
            if (Same(candidate, value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Same(T? a, T? b) => EqualityComparer<T?>.Default.Equals(a, b);

    private static string OptionText(T value) => BindingHelpers.FormatValue(value);
}
