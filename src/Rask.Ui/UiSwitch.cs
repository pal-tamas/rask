using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
/// Flux UI's switch: <c>Ui.Switch.Bind(() =&gt; m.Alerts).Label("Email alerts")</c>.
/// </summary>
/// <remarks>
/// <para>
/// Turns a setting on or off, now — where a checkbox states a fact a form submits later. A form control over
/// a <c>bool</c>: <c>.Bind(() =&gt; model.Alerts)</c> two-way binds and drives the form's validation;
/// <c>.Value(on)</c> with <c>OnChange</c> leaves the state with the parent.
/// </para>
/// <para>
/// The root (<c>data-ui-switch</c>) is a <c>&lt;label&gt;</c> holding a real
/// <c>&lt;input type="checkbox" role="switch"&gt;</c>, so the space bar and a <c>&lt;label for&gt;</c> are the
/// browser's, and the thumb moves on the input's own <c>:checked</c>. <c>Label</c> and <c>Description</c>
/// draw a <see cref="UiField" /> around it, the switch at the far edge unless <see cref="Align" /> says left.
/// </para>
/// </remarks>
public sealed partial class UiSwitch : Component, IFormControl<bool>, IUiFormControl
{
    // 32×20, the accent when on; in dark an outline that the fill replaces. 150ms on Tailwind's default curve.
    private const string Track =
        "group/switch relative flex h-5 w-8 min-w-8 items-center rounded-full transition "
        + "bg-zinc-800/15 has-checked:bg-fx-accent "
        + "dark:border dark:border-white/20 dark:bg-transparent dark:has-checked:border-0 dark:has-checked:bg-fx-accent "
        + "has-disabled:opacity-50 dark:has-disabled:border-white/10 "
        + "outline-offset-2 " + UiOptionLook.Focus;

    // 14px, 3px from the edge it rests against: 12px of travel.
    private const string Thumb =
        "size-3.5 rounded-full bg-white transition translate-x-[3px] dark:translate-x-[2px] "
        + "group-has-checked/switch:translate-x-[15px] group-has-checked/switch:bg-fx-accent-foreground";

    private string? _ownId;

    /// <summary>The words beside the switch.</summary>
    public string? Label { get; set; }

    /// <summary>Help text under the label.</summary>
    public string? Description { get; set; }

    /// <summary>After its label at the far edge of the row, or before it.</summary>
    public Ui.SwitchAlign? Align { get; set; }

    /// <summary>Cannot be switched and is skipped by the keyboard.</summary>
    public bool? Disabled { get; set; }

    /// <summary>
    ///     Attributes for the <c>&lt;input&gt;</c> itself, written as given — what Flux forwards to it:
    ///     <c>.Attributes(("aria-label", "Select row"))</c>, <c>("required", "")</c>. A <c>name</c> given here wins over
    ///     <see cref="Name" />.
    /// </summary>
    public IReadOnlyDictionary<string, string?>? Attributes { get; set; }

    /// <summary>The <c>name</c> the switch posts under in a form: the <c>&lt;input&gt;</c>'s own attribute.</summary>
    public string? Name { get; set; }

    /// <inheritdoc cref="Element.Id" />
    /// <remarks>On the <c>&lt;input&gt;</c>, which is what a <c>&lt;label for&gt;</c> names and a test clicks.</remarks>
    public string? Id { get; set; }

    /// <inheritdoc cref="IUiFormControl.ShowValidation" />
    public bool? ShowValidation { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Value" />
    /// <remarks>
    ///     Not nullable, and that is the interface rather than a choice here: <c>IFormControl&lt;T&gt;</c> declares
    ///     <c>T? Value</c> over an unconstrained <c>T</c>, where <c>?</c> is a nullability annotation — so closing it
    ///     to a value type gives a plain <c>bool</c>. A controlled switch therefore states whether it is on.
    /// </remarks>
    public bool Value { get; set; }

    /// <inheritdoc cref="IFormControl{T}.OnChange" />
    public Callback<bool> OnChange { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Bind" />
    public Expression<Func<bool>>? Bind { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Validate" />
    public Validator<bool>? Validate { get; set; }

    /// <inheritdoc cref="IFormControl{T}.AfterBind" />
    public Callback<bool> AfterBind { get; set; }

    string IUiFieldControl.ControlId => Id is null && Bind is null && Label is null
        ? _ownId ??= UiFieldId.Own(UiInstanceCounter.Next())
        : UiFieldId.Derive(Id, Bind, Label);

    LambdaExpression? IUiFieldControl.Bound => Bind;

    string? IUiFormControl.DescriptionTrailing => null;

    string? IUiFormControl.Badge => null;

    bool? IUiFormControl.Invalid => null;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var field = UiWithField.For(this);
        var on = Bind is { } bound ? ExpressionAccessor.Parse(bound).Getter() is true : Value;

        // Bind and Value are the two openings of Core's input. Unbound it is an input over no value at all,
        // so a plain form posts the browser's "on" rather than a bool's "False".
        Component input = Bind is { } bind
            ? Finish(Input.Bind(bind).Validate(Validate).AfterBind(AfterBind), field)
            : Finish(Input.Value((string?)null).Type(InputType.Checkbox).Checked(Value).OnChange(Changed), field);

        // RaskMarkup.Label, qualified: this type's Label property hides the chain entry of the same name.
        var track = RaskMarkup.Label.Class(UiClass.Compose(Track, Class)).Attributes(Marks(on))[input, Span.Class(Thumb)];

        return field.Wrap(track, Ui.FieldVariant.Inline, controlFirst: Align == Ui.SwitchAlign.Left);
    }

    private HTMLInputElement<T> Finish<T>(HTMLInputElement<T> input, UiWithField field) =>
        input.Id(field.ControlId)
            .Name(Attributes?.ContainsKey("name") == true ? null : Name)
            .Role("switch")
            .Disabled(Disabled == true)
            .Aria(field.Aria)
            .Attributes(UiOptionLook.Marks(Attributes))
            .Class(UiOptionLook.Native);

    // The browser reports the switch's state, not a toggle: a missed frame cannot leave the two out of step.
    private Task Changed(string state) => OnChange.Invoke(bool.TryParse(state, out var on) && on).AsTask();

    private static Dictionary<string, string?> Marks(bool on)
    {
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["data-" + UiOptionLook.ControlMark] = "",
            ["data-ui-switch"] = "",
        };

        if (on)
        {
            marks["data-checked"] = "";
        }

        return marks;
    }
}
