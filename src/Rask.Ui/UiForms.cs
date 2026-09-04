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

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Input
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
                Class));
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
    /// <summary>daisyUI's shape class, for example <c>mask-squircle</c>.</summary>
    public required string Shape { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("mask", Shape, Class))[Children ?? []];
}
