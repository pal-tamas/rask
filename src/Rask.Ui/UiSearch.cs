using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Ui;

/// <summary>
/// A search field: a leading icon, and the filter it drives.
/// </summary>
/// <remarks>
/// <para>
/// The accessible name is required and separate from the placeholder, which is not one — a placeholder
/// disappears exactly when typing starts, taking the field's only label with it.
/// </para>
/// <para>
/// A form control, like every other field in the kit: <c>.Bind(() =&gt; model.Query)</c> two-way binds
/// and drives the surrounding <c>Form</c>'s validation, or <see cref="Value" /> lets the parent own the
/// text. It used to carry a single bespoke <c>OnSearch</c> instead, which meant a search box was the one
/// field here that could not be bound, validated, or told when to fire.
/// </para>
/// <para>
/// <b>Two callbacks, because a search box has two moments.</b> <see cref="OnChange" /> is the COMMIT —
/// blur or Enter — and is what a page that navigates on search wants, since one navigation per
/// keystroke is not a feature. <see cref="OnInput" /> fires on every keystroke and is what an in-page
/// filter wants, where waiting for a blur means typing a query and watching nothing happen. Both are the
/// framework's own recognised names (<c>Input</c> and <c>Textarea</c> declare the same pair), so the
/// factory generator excludes them from the bound factory for free.
/// </para>
/// </remarks>
public sealed partial class UiSearch : Component, IFormControl<string>
{
    public required string Placeholder { get; set; }

    /// <summary>
    ///     The accessible name. Named for what it is rather than called <c>Label</c>, because inside a
    ///     markup host a property of that name would shadow the chain's entry for the <c>&lt;label&gt;</c>
    ///     element this component renders.
    /// </summary>
    public required string AccessibleLabel { get; set; }

    public UiSize? Size { get; set; }

    /// <summary>
    ///     Fills its container instead of settling at a fixed column from <c>sm</c> up.
    /// </summary>
    /// <remarks>
    ///     The default (<c>w-full sm:w-72</c>) suits a toolbar, where the field sits beside other things.
    ///     In a narrow column — a sidebar rail — 288px is wider than the rail itself, and this is not
    ///     something a caller can correct with <see cref="Class" />: both widths are <c>sm:</c> utilities,
    ///     and which one wins is decided by stylesheet order rather than by the order they appear in the
    ///     class attribute.
    /// </remarks>
    public bool? Block { get; set; }

    public bool? Disabled { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    public string? Value { get; set; }

    /// <inheritdoc />
    public Action<string>? OnChange { get; set; }

    /// <inheritdoc />
    public Func<string, Task>? OnChangeAsync { get; set; }

    /// <summary>
    ///     Called on every keystroke, for a filter that narrows a list as it is typed.
    /// </summary>
    /// <remarks>
    ///     Controlled mode only — a bound field installs its own <c>oninput</c> write-back and never reads
    ///     this, which is why the factory generator drops it from the bound factory.
    /// </remarks>
    public Action<string>? OnInput { get; set; }

    /// <summary>The <see langword="async" /> form of <see cref="OnInput" />.</summary>
    public Func<string, Task>? OnInputAsync { get; set; }

    /// <inheritdoc />
    public Expression<Func<string>>? Bind { get; set; }

    /// <inheritdoc />
    public Validate<string>? Validate { get; set; }

    /// <inheritdoc />
    public ValidateAsync<string>? ValidateAsync { get; set; }

    /// <inheritdoc />
    public Action<string>? AfterBind { get; set; }

    /// <inheritdoc />
    public Func<string, Task>? AfterBindAsync { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        // Bound and controlled are different chain TYPES rather than two settings on one — Bind and Value
        // are mutually exclusive openings, so the two chains are Build<Input<string>, Bound> and
        // Build<Input<string>, Controlled>. They therefore cannot be the two arms of one conditional
        // (CS0173: no implicit conversion between them); each is built and wrapped on its own, which is
        // the same shape UiInput uses for the same reason.
        if (Bind is { } bind)
        {
            return Box(
                Input
                    .Bind(bind)
                    .Validate(Validate)
                    .ValidateAsync(ValidateAsync)
                    .AfterBind(AfterBind)
                    .AfterBindAsync(AfterBindAsync)
                    .Type(InputType.Search)
                    .Placeholder(Placeholder)
                    .Aria(Aria())
                    .Disabled(Disabled == true)
                    .Class("grow"));
        }

        return Box(
            Input
                .Value(Value ?? string.Empty)
                .OnChange(OnChange)
                .OnChangeAsync(OnChangeAsync)
                .OnInput(OnInput)
                .OnInputAsync(OnInputAsync)
                .Type(InputType.Search)
                .Placeholder(Placeholder)
                .Aria(Aria())
                .Disabled(Disabled == true)
                .Class("grow"));
    }

    // daisyUI's `input` is a WRAPPER that lays out whatever sits inside it, so the icon goes in the
    // label beside the field rather than being absolutely positioned over it. Full width on a phone, and
    // a sane column from sm up unless Block says otherwise: on a 360px screen a fixed-width search box
    // either overflows the row or leaves the rest of it stranded.
    private Component Box(Component field) =>
        Label
            .Class(UiClass.Compose(
                Block == true ? "input w-full" : "input w-full sm:w-72",
                Size is { } size ? UiClassNames.InputSize(size) : "",
                Class))[
            UiIcon.Name(UiIconName.Search).Class("size-4 shrink-0 opacity-60"),
            field
        ];

    private Dictionary<string, string?> Aria() =>
        new() { ["label"] = AccessibleLabel };
}
