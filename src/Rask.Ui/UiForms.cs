namespace Rask.Ui;

/// <summary>
/// A text field.
/// </summary>
/// <remarks>
/// daisyUI's <c>input</c>. <see cref="Tone" /> colours the border, which is how a field says it is in
/// error without a second element; <see cref="UiValidator" /> is the version that says why.
/// </remarks>
public sealed partial class UiInput : Component
{
    /// <summary>
    ///     daisyUI and MaryUI both call this <c>label</c>. Free to use here because this component renders no
    ///     &lt;label&gt; element of its own — where one does, the property is AccessibleLabel instead.
    /// </summary>
    /// <remarks>Rendered as <c>aria-label</c>: a placeholder is not a name, it vanishes when typing starts.</remarks>
    public required string Label { get; set; }

    public string? Value { get; set; }

    public string? Placeholder { get; set; }

    public InputType? Type { get; set; }

    public UiTone? Tone { get; set; }

    public UiSize? Size { get; set; }

    public UiVariant? Variant { get; set; }

    public bool? Disabled { get; set; }

    public Action<string>? OnChange { get; set; }

    public Func<string, Task>? OnChangeAsync { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var input = Input
            .Value(Value ?? string.Empty)
            .Type(Type ?? InputType.Text)
            .Placeholder(Placeholder ?? string.Empty)
            .Aria(new Dictionary<string, string?> { ["label"] = Label })
            .Disabled(Disabled == true)
            .Class(UiClass.Compose(
                "input",
                Tone is { } tone ? UiClassNames.InputTone(tone) : "",
                Size is { } size ? UiClassNames.InputSize(size) : "",
                Variant is { } variant ? UiClassNames.InputVariant(variant) : "",
                Class));

        if (OnChangeAsync is { } async)
        {
            return input.OnChangeAsync(async);
        }

        return OnChange is { } sync ? input.OnChange(sync) : input;
    }
}

/// <summary>
/// A multi-line text field.
/// </summary>
public sealed partial class UiTextarea : Component
{
    /// <summary>
    ///     daisyUI and MaryUI both call this <c>label</c>. Free to use here because this component renders no
    ///     &lt;label&gt; element of its own — where one does, the property is AccessibleLabel instead.
    /// </summary>
    public required string Label { get; set; }

    public string? Value { get; set; }

    public string? Placeholder { get; set; }

    public int? Rows { get; set; }

    public UiTone? Tone { get; set; }

    /// <summary>
    ///     daisyUI defines only <see cref="UiVariant.Ghost" /> for a text control — the borderless form
    ///     that shows its edges on focus. The rest draw the default rather than a class that does nothing.
    /// </summary>
    public UiVariant? Variant { get; set; }

    public UiSize? Size { get; set; }

    public bool? Disabled { get; set; }

    public Action<string>? OnChange { get; set; }

    public Func<string, Task>? OnChangeAsync { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var area = Textarea
            .Value(Value ?? string.Empty)
            .Placeholder(Placeholder ?? string.Empty)
            .Rows(Rows ?? 3)
            .Aria(new Dictionary<string, string?> { ["label"] = Label })
            .Disabled(Disabled == true)
            .Class(UiClass.Compose(
                "textarea",
                Tone is { } tone ? UiClassNames.TextareaTone(tone) : "",
                Variant is { } variant ? UiClassNames.TextareaVariant(variant) : "",
                Size is { } size ? UiClassNames.TextareaSize(size) : "",
                Class));

        if (OnChangeAsync is { } async)
        {
            return area.OnChangeAsync(async);
        }

        return OnChange is { } sync ? area.OnChange(sync) : area;
    }
}

/// <summary>
/// A file picker.
/// </summary>
public sealed partial class UiFileInput : Component
{
    /// <summary>
    ///     daisyUI and MaryUI both call this <c>label</c>. Free to use here because this component renders no
    ///     &lt;label&gt; element of its own — where one does, the property is AccessibleLabel instead.
    /// </summary>
    public required string Label { get; set; }

    public UiTone? Tone { get; set; }

    /// <summary>
    ///     daisyUI defines only <see cref="UiVariant.Ghost" /> for a file input. The rest draw the
    ///     default rather than a class that does nothing.
    /// </summary>
    public UiVariant? Variant { get; set; }

    public UiSize? Size { get; set; }

    public bool? Disabled { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Input
            .Value(string.Empty)
            .Type(InputType.File)
            .Aria(new Dictionary<string, string?> { ["label"] = Label })
            .Disabled(Disabled == true)
            .Class(UiClass.Compose(
                "file-input",
                Tone is { } tone ? UiClassNames.FileInputTone(tone) : "",
                Variant is { } variant ? UiClassNames.FileInputVariant(variant) : "",
                Size is { } size ? UiClassNames.FileInputSize(size) : "",
                Class));
}

/// <summary>
/// A checkbox and the label that says what ticking it means.
/// </summary>
/// <remarks>
/// The label wraps the box rather than sitting beside it, so the words are part of the hit target — on a
/// phone, a 16px box on its own is the difference between a control and a dare.
/// </remarks>
public sealed partial class UiCheckbox : Component
{
    /// <summary>
    ///     The words beside the control, daisyUI's <c>label-text</c>. Not <c>Label</c>, which MaryUI uses:
    ///     this renders a &lt;label&gt; element and a property of that name would shadow its chain entry.
    ///     <c>new</c> because the base type has a markup entry called <c>Text</c>, which this does not use.
    /// </summary>
    public new required string Text { get; set; }

    public bool? Checked { get; set; }

    public UiTone? Tone { get; set; }

    public UiSize? Size { get; set; }

    public bool? Disabled { get; set; }

    public Action<bool>? OnChange { get; set; }

    public Func<bool, Task>? OnChangeAsync { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var box = Input
            .Value(Checked == true)
            .Disabled(Disabled == true)
            .Class(UiClass.Compose(
                "checkbox",
                Tone is { } tone ? UiClassNames.CheckboxTone(tone) : "",
                Size is { } size ? UiClassNames.CheckboxSize(size) : ""));

        if (OnChangeAsync is { } async)
        {
            box = box.OnChangeAsync(async);
        }
        else if (OnChange is { } sync)
        {
            box = box.OnChange(sync);
        }

        return Label.Class(UiClass.Compose("label cursor-pointer gap-2", Class))[box, Span[Text]];
    }
}

/// <summary>
/// A switch. The same input as <see cref="UiCheckbox" />, drawn as a toggle.
/// </summary>
/// <remarks>
/// Drawn differently and meant differently: a checkbox states a fact that is submitted later, a toggle
/// reads as taking effect now. Use it where flipping it does something.
/// </remarks>
public sealed partial class UiToggle : Component
{
    /// <summary>
    ///     The words beside the control, daisyUI's <c>label-text</c>. Not <c>Label</c>, which MaryUI uses:
    ///     this renders a &lt;label&gt; element and a property of that name would shadow its chain entry.
    ///     <c>new</c> because the base type has a markup entry called <c>Text</c>, which this does not use.
    /// </summary>
    public new required string Text { get; set; }

    public bool? Checked { get; set; }

    public UiTone? Tone { get; set; }

    public UiSize? Size { get; set; }

    public bool? Disabled { get; set; }

    public Action<bool>? OnChange { get; set; }

    public Func<bool, Task>? OnChangeAsync { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var box = Input
            .Value(Checked == true)
            .Disabled(Disabled == true)
            .Class(UiClass.Compose(
                "toggle",
                Tone is { } tone ? UiClassNames.ToggleTone(tone) : "",
                Size is { } size ? UiClassNames.ToggleSize(size) : ""));

        if (OnChangeAsync is { } async)
        {
            box = box.OnChangeAsync(async);
        }
        else if (OnChange is { } sync)
        {
            box = box.OnChange(sync);
        }

        return Label.Class(UiClass.Compose("label cursor-pointer gap-2", Class))[box, Span[Text]];
    }
}

/// <summary>
/// One option in a radio group.
/// </summary>
/// <remarks>
/// <see cref="Group" /> is required and is the browser's own grouping mechanism: radios with the same
/// <c>name</c> are mutually exclusive, and radios without one are not a group at all — they are several
/// independent controls that happen to look alike.
/// </remarks>
public sealed partial class UiRadio : Component
{
    /// <summary>
    ///     The words beside the control, daisyUI's <c>label-text</c>. Not <c>Label</c>, which MaryUI uses:
    ///     this renders a &lt;label&gt; element and a property of that name would shadow its chain entry.
    ///     <c>new</c> because the base type has a markup entry called <c>Text</c>, which this does not use.
    /// </summary>
    public new required string Text { get; set; }

    /// <summary>The <c>name</c> every option in the group shares. Without it there is no group.</summary>
    public required string Group { get; set; }

    public bool? Checked { get; set; }

    public UiTone? Tone { get; set; }

    public UiSize? Size { get; set; }

    public bool? Disabled { get; set; }

    public Action? OnSelected { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var box = Input
            .Value(Checked == true)
            .Type(InputType.Radio)
            .Name(Group)
            .Disabled(Disabled == true)
            .Class(UiClass.Compose(
                "radio",
                Tone is { } tone ? UiClassNames.RadioTone(tone) : "",
                Size is { } size ? UiClassNames.RadioSize(size) : ""));

        if (OnSelected is { } selected)
        {
            // The bool is the input's own checked state, and a radio only ever reports true — selecting
            // one does not fire a change on the option it deselected. So the callback takes nothing:
            // "this option was chosen" is the whole event.
            box = box.OnChange(_ => selected());
        }

        return Label.Class(UiClass.Compose("label cursor-pointer gap-2", Class))[box, Span[Text]];
    }
}

/// <summary>
/// A slider.
/// </summary>
public sealed partial class UiRange : Component
{
    /// <summary>
    ///     daisyUI and MaryUI both call this <c>label</c>. Free to use here because this component renders no
    ///     &lt;label&gt; element of its own — where one does, the property is AccessibleLabel instead.
    /// </summary>
    /// <remarks>A slider with no name announces only a number.</remarks>
    public required string Label { get; set; }

    public double? Value { get; set; }

    public double? Min { get; set; }

    public double? Max { get; set; }

    public double? Step { get; set; }

    public UiTone? Tone { get; set; }

    public UiSize? Size { get; set; }

    /// <summary>
    ///     Stands the track on end. daisyUI rotates the control, so the low value is at the BOTTOM —
    ///     which is what a volume or a level wants and what a rank does not.
    /// </summary>
    public bool? Vertical { get; set; }

    /// <summary>
    ///     Runs as the handle moves, with the value the reader has landed on. Without it the control
    ///     draws a value and reports nothing, which is a slider you can push and cannot read.
    /// </summary>
    public Action<double>? OnChange { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var range = Input
            .Value((Value ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Type(InputType.Range)
            .Min((Min ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Max((Max ?? 100).ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Step((Step ?? 1).ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Aria(new Dictionary<string, string?> { ["label"] = Label })
            .Class(UiClass.Compose(
                "range",
                Tone is { } tone ? UiClassNames.RangeTone(tone) : "",
                Size is { } size ? UiClassNames.RangeSize(size) : "",
                Vertical == true ? "range-vertical" : "",
                Class));

        return OnChange is { } change
            // The browser hands back a string; a range whose value did not parse is a browser bug, and
            // falling back to the current value is quieter than throwing at a reader mid-drag.
            ? range.OnChange(text => change(
                double.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : Value ?? 0))
            : range;
    }
}

/// <summary>
/// A group of controls under one caption, with optional help text.
/// </summary>
/// <remarks>
/// A real <c>&lt;fieldset&gt;</c> and <c>&lt;legend&gt;</c> rather than a styled div: it is what gives a
/// screen reader the relationship between the caption and the controls, and what lets a browser disable
/// the whole group at once.
/// </remarks>
public sealed partial class UiFieldset : Component
{
    /// <summary>
    ///     The words beside the control, daisyUI's <c>label-text</c>. Not <c>Label</c>, which MaryUI uses:
    ///     this renders a &lt;label&gt; element and a property of that name would shadow its chain entry.
    ///     <c>new</c> because the base type has a markup entry called <c>Text</c>, which this does not use.
    /// </summary>
    public new required string Text { get; set; }

    public string? Help { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Fieldset.Class(UiClass.Compose("fieldset", Class))[
            Legend.Class("fieldset-legend")[Title],
            Children ?? [],
            Help is { } help ? P.Class("label")[help] : null
        ];
}

/// <summary>
/// The message a field shows when what was typed is not acceptable.
/// </summary>
/// <remarks>
/// Pair it with a field carrying <see cref="UiTone.Error" />. daisyUI's <c>validator-hint</c> is hidden
/// until the input beside it is invalid, so the message occupies its space whether or not it is showing
/// and the form does not jump as the reader types.
/// </remarks>
public sealed partial class UiValidator : Component
{
    public required string Message { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        P.Class(UiClass.Compose("validator-hint", Class))[Message];
}

/// <summary>
/// A field with a fixed set of answers.
/// </summary>
/// <remarks>
/// A real &lt;select&gt; rather than a styled list, which is what makes it work with a keyboard, a screen
/// reader and a phone's native picker without a line of script.
/// </remarks>
public sealed partial class UiSelect : Component
{
    /// <summary>The accessible name.</summary>
    public required string Label { get; set; }

    /// <summary>The options: the value stored, and the words shown.</summary>
    public required IReadOnlyList<(string Value, string Text)> Options { get; set; }

    public string? Value { get; set; }

    /// <summary>Shown first and unselectable — the prompt, not an answer.</summary>
    public string? Placeholder { get; set; }

    public UiTone? Tone { get; set; }

    public UiSize? Size { get; set; }

    public UiVariant? Variant { get; set; }

    public bool? Disabled { get; set; }

    public Action<string>? OnChange { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var select = Select
            .Value(Value ?? string.Empty)
            .Aria(new Dictionary<string, string?> { ["label"] = Label })
            .Disabled(Disabled == true)
            .Class(UiClass.Compose(
                "select",
                Tone is { } tone ? UiClassNames.SelectTone(tone) : "",
                Size is { } size ? UiClassNames.SelectSize(size) : "",
                Variant is { } variant ? UiClassNames.SelectVariant(variant) : "",
                Class));

        if (OnChange is { } change)
        {
            select = select.OnChange(change);
        }

        return select[
            // `disabled` as well as empty: a placeholder that can be chosen is an answer, and one chosen
            // by accident is a bug report about a form that saved nothing.
            Placeholder is { } placeholder
                ? Option.Value(string.Empty).Disabled(true).Selected(Value is null)[placeholder]
                : null,
            Options.Select(o => Option.Key(o.Value).Value(o.Value).Selected(o.Value == Value)[o.Text])
        ];
    }
}

/// <summary>
/// An image cropped to a shape.
/// </summary>
/// <remarks>
/// The shape is daisyUI's own class — <c>mask-squircle</c>, <c>mask-hexagon</c>, <c>mask-star</c> — passed
/// through rather than enumerated, because the set is long, purely decorative, and grows without the kit
/// having anything to say about it.
/// </remarks>
public sealed partial class UiMask : Component
{
    /// <summary>The shape it is clipped to.</summary>
    public required UiMaskShape Shape { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("mask", UiClassNames.MaskShape(Shape), Class))[Children ?? []];
}

/// <summary>
/// A caption attached to a control, inside the control's own frame.
/// </summary>
/// <remarks>
/// daisyUI's <c>label</c>, which draws the text as part of the field rather than above it — a currency
/// beside an amount, a unit beside a number, <c>https://</c> before a domain.
/// <b>It is decoration, not a name.</b> A <c>&lt;label&gt;</c> element names a control for assistive
/// technology; this styles text next to one. The control still needs its own name, which every kit
/// control takes as a required property.
/// </remarks>
public sealed partial class UiLabel : Component
{
    /// <summary>The text before the control.</summary>
    public new string? Text { get; set; }

    /// <summary>The text after it.</summary>
    public string? Trailing { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Label.Class(UiClass.Compose("label", Class))[
            Text is null ? null : Span[Text],
            Children ?? [],
            Trailing is null ? null : Span[Trailing]
        ];
}

/// <summary>
/// A caption that sits in the field until it has content, then rises above it.
/// </summary>
/// <remarks>
/// <para>
/// Worth preferring over a placeholder-as-label, which is the pattern this replaces: a placeholder
/// disappears the moment typing starts, so the one thing saying what the field is for vanishes exactly
/// when a reader might check it, and it is invisible to anybody reviewing a filled-in form.
/// </para>
/// <para>
/// The text must come FIRST inside the wrapper — daisyUI selects the control as the sibling after it.
/// </para>
/// </remarks>
public sealed partial class UiFloatingLabel : Component
{
    /// <summary>What the field is for.</summary>
    public new required string Text { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Label.Class(UiClass.Compose("floating-label", Class))[
            Span[Text],
            Children ?? []
        ];
}

/// <summary>
/// A short code, one character to a box.
/// </summary>
/// <remarks>
/// <para>
/// <b>One input, drawn as several.</b> The obvious build — one <c>&lt;input&gt;</c> per digit — is the
/// one to avoid: it needs script to move focus between the boxes, it defeats the browser's own SMS
/// autofill, and pasting a code lands the whole string in the first box. daisyUI's <c>otp</c> draws the
/// separators over a single field, so paste, autofill, backspace and select-all are the platform's.
/// </para>
/// <para>
/// <c>autocomplete="one-time-code"</c> and <c>inputmode="numeric"</c> are what tell a phone to offer
/// the code from the message it just received and to show the number pad; without them this is a text
/// box that happens to look like a code field.
/// </para>
/// </remarks>
public sealed partial class UiOtp : Component
{
    /// <summary>The accessible name — what the code is for.</summary>
    public required string Label { get; set; }

    /// <summary>How many characters. daisyUI draws the boxes from this.</summary>
    public required int Length { get; set; }

    public string? Value { get; set; }

    /// <summary>Runs the code up to the caller as it is typed.</summary>
    public Action<string>? OnChange { get; set; }

    /// <summary>Runs once the code is <see cref="Length" /> characters long.</summary>
    public Action<string>? OnComplete { get; set; }

    /// <summary>Draws the boxes joined into one block rather than separated.</summary>
    public bool? Joined { get; set; }

    public UiTone? Tone { get; set; }

    public UiSize? Size { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var field = Input
            .Value(Value ?? string.Empty)
            .Type(InputType.Text)
            .Aria(new Dictionary<string, string?> { ["label"] = Label })
            .Class(UiClass.Compose(
                "otp",
                Joined == true ? "otp-joined" : "",
                Tone is { } tone ? UiClassNames.OtpTone(tone) : "",
                Size is { } size ? UiClassNames.OtpSize(size) : "",
                Class))
            .Attributes(
                // daisyUI counts the boxes from this, and it also stops a reader typing past the end.
                ("maxlength", Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("autocomplete", "one-time-code"),
                ("inputmode", "numeric"),
                ("pattern", "[0-9]*"));

        if (OnChange is null && OnComplete is null)
        {
            return field;
        }

        return field.OnChange(value =>
        {
            OnChange?.Invoke(value);

            // Fires on the transition INTO a complete code, not on every keystroke while it is
            // complete: a caller that submits from here would otherwise submit on every edit.
            if (OnComplete is { } complete && value.Length == Length && (Value?.Length ?? 0) != Length)
            {
                complete(value);
            }
        });
    }
}

/// <summary>
/// A row of choices where picking one narrows a list, with a way back to all of them.
/// </summary>
/// <remarks>
/// <para>
/// Radios rather than buttons, and that is what makes it work with no script: daisyUI's <c>filter</c>
/// hides the unpicked options once one is chosen and shows the reset in their place, entirely in CSS.
/// The group also gives a keyboard the arrow-key behaviour a row of buttons would have to reimplement.
/// </para>
/// <para>
/// <see cref="Selected" /> and <see cref="OnSelect" /> keep C# in step, so the page can act on the
/// choice and restore it later.
/// </para>
/// </remarks>
public sealed partial class UiFilter : Component
{
    /// <summary>The radio group's name, so two filters on one page do not fight.</summary>
    public required string Group { get; set; }

    /// <summary>The options offered, in order.</summary>
    public required IReadOnlyList<string> Options { get; set; }

    /// <summary>The chosen option, or <c>null</c> for none.</summary>
    public string? Selected { get; set; }

    /// <summary>Runs with the option chosen, or <c>null</c> when the reset is pressed.</summary>
    public Action<string?>? OnSelect { get; set; }

    /// <summary>The accessible name on the reset control. Defaults to "All".</summary>
    public string? ResetLabel { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var reset = Input
            .Value(Selected is null)
            .Type(InputType.Radio)
            .Name(Group)
            .Class("btn btn-square filter-reset")
            .Aria(new Dictionary<string, string?> { ["label"] = ResetLabel ?? "All" });

        if (OnSelect is { } onReset)
        {
            reset = reset.OnChange(_ => onReset(null));
        }

        // A div, not a <form>. daisyUI's own example wraps this in one so a reset BUTTON can clear it,
        // but the reset here is a radio carrying `filter-reset` — the group already holds the state,
        // and a nested <form> inside somebody else's form is invalid HTML.
        return Div.Class(UiClass.Compose("filter", Class))[
            reset,
            Options.Select(option =>
            {
                var choice = Input
                    .Value(Selected == option)
                    .Key(option)
                    .Type(InputType.Radio)
                    .Name(Group)
                    .Class("btn")
                    // daisyUI draws the option's text from the input's own value, so this is the label
                    // as well as the value.
                    .Attributes(("value", option))
                    .Aria(new Dictionary<string, string?> { ["label"] = option });

                return OnSelect is { } select ? choice.OnChange(_ => select(option)) : choice;
            })
        ];
    }
}

/// <summary>
/// A month, with a day to pick.
/// </summary>
/// <remarks>
/// <para>
/// <b>Built here in C#, not from a web component.</b> daisyUI styles the third-party <c>cally</c>
/// element, which is JavaScript this kit does not ship — so the grid is laid out in C#, over daisyUI's
/// <c>calendar</c> classes, and moving between months is an ordinary re-render.
/// </para>
/// <para>
/// The days are <c>&lt;button&gt;</c> elements in a table, so a keyboard reaches every one and a screen
/// reader gets the column headers with them. Each carries its full date as its accessible name: "14"
/// on its own is not something you can act on when the month has scrolled out of earshot.
/// </para>
/// <para>
/// It has no text field of its own. Pair it with one where a date can also be typed — typing is faster
/// than paging through months for anything more than a few weeks away, and it is the only route for
/// somebody who cannot use a pointer comfortably.
/// </para>
/// </remarks>
public sealed partial class UiCalendar : Component
{
    /// <summary>The accessible name — what the date is for.</summary>
    public required string Label { get; set; }

    /// <summary>Any day in the month being shown. Defaults to the month of the selected day, or today.</summary>
    public DateOnly? Month { get; set; }

    /// <summary>Runs with the first day of the month the reader asked for.</summary>
    public Action<DateOnly>? OnMonth { get; set; }

    /// <summary>The chosen day.</summary>
    public DateOnly? Selected { get; set; }

    /// <summary>Runs with the day the reader picked.</summary>
    public Action<DateOnly>? OnSelect { get; set; }

    /// <summary>The earliest selectable day. Days before it are disabled rather than hidden.</summary>
    public DateOnly? Min { get; set; }

    /// <summary>The latest selectable day.</summary>
    public DateOnly? Max { get; set; }

    /// <summary>Which day the week starts on. Defaults to Monday.</summary>
    public DayOfWeek? FirstDay { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var shown = Month ?? Selected ?? DateOnly.FromDateTime(DateTime.Today);
        var first = new DateOnly(shown.Year, shown.Month, 1);
        var firstDay = FirstDay ?? DayOfWeek.Monday;

        // How many blanks before the 1st. The +7 keeps it non-negative whichever day the week starts on.
        var lead = ((int)first.DayOfWeek - (int)firstDay + 7) % 7;
        var days = DateTime.DaysInMonth(first.Year, first.Month);

        return Div
            .Role("group")
            .Aria(new Dictionary<string, string?> { ["label"] = Label })
            .Class(UiClass.Compose("calendar rounded-box border border-base-300 bg-base-100 p-3", Class))[
            Div.Class("mb-2 flex items-center justify-between gap-2")[
                MonthStep("prev", first.AddMonths(-1), "Previous month", UiIconName.ArrowLeft),
                Div.Class("text-sm font-semibold")[
                    first.ToString("MMMM yyyy", System.Globalization.CultureInfo.CurrentCulture)
                ],
                MonthStep("next", first.AddMonths(1), "Next month", UiIconName.ArrowRight)
            ],
            Table.Class("calendar-month w-full")[
                Thead[
                    Tr[
                        Enumerable.Range(0, 7).Select(i =>
                        {
                            var day = (DayOfWeek)(((int)firstDay + i) % 7);
                            var name = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat
                                .GetShortestDayName(day);

                            return Th.Key(day).Class("text-xs font-normal opacity-60")[name];
                        })
                    ]
                ],
                Tbody[
                    Enumerable.Range(0, (lead + days + 6) / 7).Select(week =>
                        Tr.Key(week)[
                            Enumerable.Range(0, 7).Select(slot =>
                            {
                                var number = (week * 7) + slot - lead + 1;

                                return number < 1 || number > days
                                    ? Td.Key(slot)
                                    : Td.Key(slot)[Day(new DateOnly(first.Year, first.Month, number))];
                            })
                        ])
                ]
            ]
        ];
    }

    private Component MonthStep(object key, DateOnly target, string label, UiIconName icon)
    {
        var button = Button
            .Key(key)
            .Type("button")
            .Class("btn btn-ghost btn-sm btn-square")
            .Aria(new Dictionary<string, string?> { ["label"] = label });

        if (OnMonth is { } onMonth)
        {
            button = button.OnClick(() => onMonth(target));
        }

        return button[UiIcon.Name(icon).Class("size-4 shrink-0")];
    }

    private Component Day(DateOnly date)
    {
        var chosen = Selected == date;
        var blocked = (Min is { } min && date < min) || (Max is { } max && date > max);

        var button = Button
            .Key(date.Day)
            .Type("button")
            .Class(UiClass.Compose("btn btn-ghost btn-sm btn-square", chosen ? "btn-active" : ""))
            .Disabled(blocked)
            .Aria(new Dictionary<string, string?>
            {
                // The full date, not the number: "14" is not something you can act on once the month
                // has scrolled out of earshot.
                ["label"] = date.ToString("D", System.Globalization.CultureInfo.CurrentCulture),
                ["pressed"] = chosen ? "true" : "false",
            });

        if (OnSelect is { } onSelect && !blocked)
        {
            button = button.OnClick(() => onSelect(date));
        }

        return button[date.Day.ToString(System.Globalization.CultureInfo.CurrentCulture)];
    }
}
