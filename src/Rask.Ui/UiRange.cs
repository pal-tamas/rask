using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Ui;

/// <summary>
/// A slider.
/// </summary>
/// <remarks>
/// A form control over a <c>double</c>, concretely rather than generically — a slider's value is a
/// number on a continuous scale, and <see cref="Min" />, <see cref="Max" /> and <see cref="Step" /> only
/// mean anything against one. <c>.Bind(() =&gt; model.Volume)</c> two-way binds; <see cref="Value" />
/// with <see cref="OnChange" /> leaves it with the parent.
/// </remarks>
public sealed partial class UiRange : Component, IFormControl<double>
{
    /// <summary>
    ///     daisyUI and MaryUI both call this <c>label</c>. Free to use here because this component renders no
    ///     &lt;label&gt; element of its own — where one does, the property is AccessibleLabel instead.
    /// </summary>
    /// <remarks>A slider with no name announces only a number.</remarks>
    public new required string Label { get; set; }

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

    public string? Class { get; set; }

    /// <inheritdoc />
    /// <remarks>
    ///     Where the handle sits. Not nullable — see <see cref="UiCheckbox.Value" /> — so a controlled
    ///     slider states its position, which it has to: a handle has to be somewhere.
    /// </remarks>
    public double Value { get; set; }

    /// <inheritdoc />
    /// <remarks>
    ///     Runs with the value the reader landed on. Without it a controlled slider draws a position and
    ///     reports nothing, which is a control you can push and cannot read.
    /// </remarks>
    public Action<double>? OnChange { get; set; }

    /// <inheritdoc />
    public Func<double, Task>? OnChangeAsync { get; set; }

    /// <inheritdoc />
    public Expression<Func<double>>? Bind { get; set; }

    /// <inheritdoc />
    public Validate<double>? Validate { get; set; }

    /// <inheritdoc />
    public ValidateAsync<double>? ValidateAsync { get; set; }

    /// <inheritdoc />
    public Action<double>? AfterBind { get; set; }

    /// <inheritdoc />
    public Func<double, Task>? AfterBindAsync { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        // The parse in both directions is the framework's: Input<double> reads the browser's string and
        // hands back a double, which is why nothing here does TryParse any more.
        if (Bind is { } bind)
        {
            return Input
                .Bind(bind)
                .Validate(Validate)
                .ValidateAsync(ValidateAsync)
                .AfterBind(AfterBind)
                .AfterBindAsync(AfterBindAsync)
                .Type(InputType.Range)
                .Min(Bound(Min ?? 0))
                .Max(Bound(Max ?? 100))
                .Step(Bound(Step ?? 1))
                .Aria(new Dictionary<string, string?> { ["label"] = Label })
                .Class(BoxClass());
        }

        return Input
            .Value(Value)
            .OnChange(OnChange)
            .OnChangeAsync(OnChangeAsync)
            .Type(InputType.Range)
            .Min(Bound(Min ?? 0))
            .Max(Bound(Max ?? 100))
            .Step(Bound(Step ?? 1))
            .Aria(new Dictionary<string, string?> { ["label"] = Label })
            .Class(BoxClass());
    }

    // Invariant culture, not the reader's: these are HTML attribute values, and a comma decimal
    // separator makes the browser drop the whole attribute.
    private static string Bound(double value) => value.ToString(CultureInfo.InvariantCulture);

    private string BoxClass() =>
        UiClass.Compose(
            "range",
            Tone is { } tone ? UiClassNames.RangeTone(tone) : "",
            Size is { } size ? UiClassNames.RangeSize(size) : "",
            Vertical == true ? "range-vertical" : "",
            Class);
}
