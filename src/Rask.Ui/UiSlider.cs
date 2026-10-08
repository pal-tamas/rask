using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
/// Flux UI's slider: <c>Ui.Slider.Bind(() =&gt; m.Amount).Min(0).Max(100).Step(10)</c>.
/// </summary>
/// <remarks>
/// <para>
/// A form control over a number — <c>int</c>, <c>long</c>, <c>float</c>, <c>double</c> or <c>decimal</c>.
/// <c>.Bind(() =&gt; model.Amount)</c> two-way binds and drives the form's validation; <c>.Value(x)</c> with
/// <c>OnChange</c> leaves the value with the parent. The value follows the thumb while it is dragged.
/// </para>
/// <para>
/// <b>A range</b> is the same slider over an array of two: <c>Ui.Slider.Bind(() =&gt; m.Price).Range()</c> with
/// <c>int[] Price = [200, 800]</c>, as Flux binds an array. Every change writes a new array. The thumbs do not
/// cross, and <see cref="MinStepsBetween" /> keeps them further apart.
/// </para>
/// <para>
/// Each thumb holds a real <c>&lt;input type="range"&gt;</c> laid over the stretch of track it can reach, so
/// dragging, pressing the track, the arrow keys, Page Up / Page Down and Home / End are the browser's own;
/// <see cref="BigStep" /> is the runtime's (<c>data-rask-big-step</c> on that input).
/// <c>Ui.SliderTick</c> children mark values under the track, or on it with <see cref="TickPosition" />.
/// </para>
/// </remarks>
public sealed partial class UiSlider<T> : Component, IFormControl<T>, IUiFormControl
{
    /// <summary>Two thumbs, over an array of two numbers.</summary>
    public bool? Range { get; set; }

    /// <summary>The lowest value. 0 unless set.</summary>
    public double? Min { get; set; }

    /// <summary>The highest value. 100 unless set.</summary>
    public double? Max { get; set; }

    /// <summary>The distance between two values the thumb can rest on. 1 unless set.</summary>
    public double? Step { get; set; }

    /// <summary>
    ///     The distance Shift with an arrow key moves the thumb, and Page Up / Page Down with it. Unset, Shift
    ///     changes nothing and the Page keys are the browser's own tenth of the track.
    /// </summary>
    public double? BigStep { get; set; }

    /// <summary>How many steps two thumbs of a range are kept apart. None unless set.</summary>
    public int? MinStepsBetween { get; set; }

    /// <summary>Whether <c>Ui.SliderTick</c> children are drawn under the track or on it.</summary>
    public Ui.SliderTickPosition? TickPosition { get; set; }

    /// <summary>Classes for the track: <c>h-5</c>.</summary>
    public string? TrackClass { get; set; }

    /// <summary>
    ///     Classes for the thumb. A <c>size-6</c> or <c>size-[22px]</c> here is also where the fill and the ticks
    ///     learn how big the thumb is.
    /// </summary>
    public string? ThumbClass { get; set; }

    /// <inheritdoc cref="UiInput{T}.Disabled" />
    public bool? Disabled { get; set; }

    /// <summary>Classes for the slider: widths and margins.</summary>
    public string? Class { get; set; }

    /// <inheritdoc cref="Element.Id" />
    public string? Id { get; set; }

    /// <inheritdoc cref="IUiFormControl.ShowValidation" />
    public bool? ShowValidation { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Value" />
    public T? Value { get; set; }

    /// <inheritdoc cref="IFormControl{T}.OnChange" />
    public Callback<T> OnChange { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Bind" />
    public Expression<Func<T>>? Bind { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Validate" />
    public Validator<T>? Validate { get; set; }

    /// <inheritdoc cref="IFormControl{T}.AfterBind" />
    public Callback<T> AfterBind { get; set; }

    string IUiFieldControl.ControlId => UiFieldId.Derive(Id, Bind, null);

    // Flux's slider takes no label of its own: its examples put it inside a flux:field.
    string? IUiFormControl.Label => null;

    string? IUiFormControl.Description => null;

    string? IUiFormControl.DescriptionTrailing => null;

    string? IUiFormControl.Badge => null;

    bool? IUiFormControl.Invalid => null;

    LambdaExpression? IUiFieldControl.Bound => Bind;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var field = UiWithField.For(this);
        var (accessor, context, current) = UiFormCommit.Resolve(this);
        var scale = UiSliderScale.Of(Min, Max, Step, MinStepsBetween);
        var live = new Live(accessor, context, scale, scale.Normalize(UiSliderValue.Read(current), IsRange()));
        var ticks = Ticks(live);
        var inside = TickPosition == Ui.SliderTickPosition.Inside;

        return field.Wrap(
            Div.Class(UiClass.Compose(UiSliderLook.Root, Class))
                .Data(("ui-slider", ""), ("ui-control", ""))
                .Style(UiSliderLook.ThumbSize(ThumbClass))[
                Div.Class(UiSliderLook.Wrapper).Data("ui-slider-track", "")[
                    Div.Class(UiClass.Compose(UiSliderLook.Track, TrackClass)).Data("ui-slider-track", "")[
                        Div.Class(UiSliderLook.Clip)[
                            Div.Class(UiSliderLook.Indicator)
                                .Data("ui-slider-indicator", "")
                                .Style(UiSliderLook.Filled(Array.ConvertAll(live.Values, scale.Fraction)))
                        ],
                        inside ? ticks : null,
                        live.Values.Select((_, thumb) => Thumb(field, live, thumb))
                    ],
                    inside ? null : ticks
                ]
            ]);
    }

    private bool IsRange()
    {
        if (Range == true && !UiSliderValue.IsPair<T>())
        {
            throw new InvalidOperationException("Ui.Slider.Range() binds an array of two numbers: int[], double[], decimal[].");
        }

        return UiSliderValue.IsPair<T>();
    }

    private Component? Ticks(Live live)
    {
        if (Children?.Any() != true)
        {
            return null;
        }

        var inside = TickPosition == Ui.SliderTickPosition.Inside;
        var scope = new UiSliderScope(live.Scale, live.Values, inside, Disabled == true ? null : to => PickAsync(live, to));

        return Div.Class(inside ? UiSliderLook.TicksInside : UiSliderLook.TicksBelow)
            .Data("ui-slider-tick-position", inside ? "inside" : "below")[
            Context.Provide(scope)[Children]
        ];
    }

    private Component Thumb(UiWithField field, Live live, int thumb)
    {
        var fraction = live.Scale.Fraction(live.Values[thumb]);

        return Div.Class(UiClass.Compose(UiSliderLook.Thumb, ThumbClass))
            .Data("ui-slider-thumb", "")
            .Style("inset-inline-start:" + UiSliderLook.At(fraction))
            .Key(thumb)[
            Control(field, live, thumb, fraction)
        ];
    }

    // One native range input per thumb, over exactly the values that thumb may take: its own `min` and `max`
    // are what stop it at its neighbour, with no script.
    private HTMLInputElement<string> Control(UiWithField field, Live live, int thumb, decimal fraction)
    {
        var (lowest, highest) = live.Scale.Reach(live.Values, thumb);
        var (low, high) = (live.Scale.Fraction(lowest), live.Scale.Fraction(highest));
        var reach = UiSliderLook.Reach(fraction, low, high);
        if (live.Values.Length == 2)
        {
            var middle = (live.Scale.Fraction(live.Values[0]) + live.Scale.Fraction(live.Values[1])) / 2;
            reach += UiSliderLook.Half(thumb, middle, low, high);
        }

        var control = Input.Value(UiSliderScale.Text(live.Values[thumb]))
            .OnInput(raw => MovedAsync(live, thumb, raw))
            .Type(InputType.Range)
            .Id(thumb == 0 ? field.ControlId : null)
            .Min(UiSliderScale.Text(lowest))
            .Max(UiSliderScale.Text(highest))
            .Step(UiSliderScale.Text(live.Scale.Step))
            .Disabled(Disabled == true)
            .Aria(Names(field, live, thumb))
            .Class(UiSliderLook.Control)
            .Style(reach);

        return BigStep is { } big and > 0 ? control.Data("rask-big-step", UiSliderScale.Text((decimal)big)) : control;
    }

    // As Flux writes them: every thumb is named by the field's label, and a range's two say which end they are.
    private static IReadOnlyDictionary<string, string?> Names(UiWithField field, Live live, int thumb)
    {
        var range = live.Values.Length == 2;
        if (!range && field.LabelledBy is null)
        {
            return field.Aria;
        }

        var names = new Dictionary<string, string?>(field.Aria, StringComparer.Ordinal);
        if (range)
        {
            names["valuetext"] = UiSliderScale.Text(live.Values[thumb]) + (thumb == 0 ? " start range" : " end range");
        }

        if (field.LabelledBy is { } label)
        {
            names["labelledby"] = label;
        }

        return names;
    }

    private Task MovedAsync(Live live, int thumb, string raw) =>
        decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var to)
            ? MoveAsync(live, thumb, to)
            : Task.CompletedTask;

    private Task PickAsync(Live live, decimal to) =>
        MoveAsync(live, UiSliderScale.Nearest(live.Values, to), to);

    private async Task MoveAsync(Live live, int thumb, decimal to)
    {
        var moved = live.Scale.Move(live.Values, thumb, to);
        if (moved.AsSpan().SequenceEqual(live.Values))
        {
            return;
        }

        await UiFormCommit.CommitAsync(this, live.Accessor, live.Context, UiSliderValue.Write<T>(moved)).ConfigureAwait(false);
    }

    // What one render resolved, for the handlers it registered.
    private sealed record Live(ExpressionAccessor.Accessor? Accessor, EditContext? Context, UiSliderScale Scale, decimal[] Values);
}
