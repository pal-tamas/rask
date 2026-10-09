using System.Linq.Expressions;
using Rask.Core.Components;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
/// Flux UI's checkbox: <c>Ui.Checkbox.Bind(() =&gt; m.Agreed).Label("I agree to the terms")</c>.
/// </summary>
/// <remarks>
/// <para>
/// On its own it holds a <c>bool</c>: <c>Bind</c> two-way binds a model member and drives the form's
/// validation, <c>Checked</c> with <c>OnChange</c> leaves the state with the parent. A member that is a
/// <c>bool?</c> binds too, and draws the dash while it is <see langword="null" />.
/// </para>
/// <para>
/// Inside a <see cref="UiCheckboxGroup{T}" /> it is one of the group's choices: <c>Value</c> is what the
/// group's collection holds while it is ticked, and the group — not the checkbox — is what is bound.
/// </para>
/// <para>
/// The root (<c>data-ui-checkbox</c>) is a <c>&lt;label&gt;</c> holding a real
/// <c>&lt;input type="checkbox"&gt;</c>, so the space bar, a <c>&lt;label for&gt;</c> and a form post are
/// the browser's. <c>Label</c> and <c>Description</c> draw a <see cref="UiField" /> around it.
/// </para>
/// </remarks>
public sealed partial class UiCheckbox : Component, IUiFormControl
{
    private UiCheckboxScope? _group;
    private string? _ownId;

    /// <summary>The words beside the box — or on the card, pill or button a group draws it as.</summary>
    public string? Label { get; set; }

    /// <summary>Help text under the label.</summary>
    public string? Description { get; set; }

    /// <summary>What the group's collection holds while this is ticked. Also the value a plain form posts.</summary>
    public object? Value { get; set; }

    /// <summary>Whether it is ticked, when nothing is bound. Pair it with <see cref="OnChange" />.</summary>
    public bool? Checked { get; set; }

    /// <summary>Draws a dash instead of a tick: some of what it stands for is selected, not all.</summary>
    public bool? Indeterminate { get; set; }

    /// <summary>Cannot be ticked and is skipped by the keyboard.</summary>
    public bool? Disabled { get; set; }

    /// <summary>Draws the error state. A bound checkbox is invalid on its own while its form holds a message for it.</summary>
    public bool? Invalid { get; set; }

    /// <summary>A Heroicon before the label of a card or a button.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>The <c>bool</c> or <c>bool?</c> member it ticks: <c>.Bind(() =&gt; model.Agreed)</c>.</summary>
    public Expression<Func<bool?>>? Bind { get; set; }

    /// <summary>A rule for the bound member, run on change and on submit.</summary>
    public Validator<bool>? Validate { get; set; }

    /// <summary>Runs after a bound write, with what was written.</summary>
    public Callback<bool> AfterBind { get; set; }

    /// <summary>Runs with the new state when it is ticked or cleared and nothing is bound.</summary>
    public Callback<bool> OnChange { get; set; }

    /// <summary>
    ///     Attributes for the <c>&lt;input&gt;</c> itself, written as given — what Flux forwards to it:
    ///     <c>.Attributes(("aria-label", "Select row"))</c>, <c>("required", "")</c>. A <c>name</c> given here wins over
    ///     <see cref="Name" />.
    /// </summary>
    public IReadOnlyDictionary<string, string?>? Attributes { get; set; }

    /// <summary>The <c>name</c> the checkbox posts under in a form: the <c>&lt;input&gt;</c>'s own attribute.</summary>
    public string? Name { get; set; }

    /// <inheritdoc cref="Element.Id" />
    /// <remarks>On the <c>&lt;input&gt;</c>, which is what a <c>&lt;label for&gt;</c> names and a test clicks.</remarks>
    public string? Id { get; set; }

    /// <inheritdoc cref="IUiFormControl.ShowValidation" />
    public bool? ShowValidation { get; set; }

    public string? Class { get; set; }

    string IUiFieldControl.ControlId =>
        Id ?? (_group is { } group ? UiFieldId.Choice(group.GroupId, Value) : Standalone());

    LambdaExpression? IUiFieldControl.Bound => _group is null ? Bind : null;

    string? IUiFormControl.DescriptionTrailing => null;

    string? IUiFormControl.Badge => null;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var scope = Context.Get<UiCheckboxScope>();
        _group = ReferenceEquals(scope, UiCheckboxScope.None) ? null : scope;
        var variant = _group?.Variant ?? Ui.CheckboxGroupVariant.Default;

        // A card, a pill and a button draw their own words: there is no field around them to describe.
        var field = variant == Ui.CheckboxGroupVariant.Default
            ? UiWithField.For(this)
            : UiWithField.For((IUiFieldControl)this, invalid: Invalid == true);
        var (on, mixed) = State();
        var input = Native(field, on, mixed);

        return variant switch
        {
            Ui.CheckboxGroupVariant.Cards => Mark(UiOptionLook.Card, "ui-checkbox-cards", field, on, mixed)[input, CardContent()],
            Ui.CheckboxGroupVariant.Pills => Mark(UiOptionLook.Pill, "ui-checkbox-pills", field, on, mixed)[input, Words()],
            Ui.CheckboxGroupVariant.Buttons => Mark(UiOptionLook.Button, "ui-checkbox-buttons", field, on, mixed)[
                input,
                Icon is { } icon ? Ui.Icon.Name(icon).Micro.Class(UiOptionLook.ButtonIcon) : null,
                Span[Words()]
            ],
            _ => field.Wrap(
                Mark(UiOptionLook.Checkbox, "ui-checkbox", field, on, mixed)[input, Ui.CheckboxIndicator],
                Ui.FieldVariant.Inline,
                controlFirst: true),
        };
    }

    // On its own: named by what it binds or says, or — with neither — by an id nothing else on the page has.
    private string Standalone() => Bind is null && Label is null
        ? _ownId ??= UiFieldId.Own(UiInstanceCounter.Next())
        : UiFieldId.Derive(null, Bind, Label);

    // Ticked, and whether the dash is drawn over it: a bound bool? that is null is neither yes nor no.
    private (bool On, bool Mixed) State()
    {
        if (_group is { } group)
        {
            return (group.Contains(Value), Indeterminate == true);
        }

        if (Bind is not { } bind)
        {
            return (Checked == true, Indeterminate == true);
        }

        var accessor = ExpressionAccessor.Parse(bind);
        var value = accessor.Getter();
        var unanswered = value is null && Nullable.GetUnderlyingType(accessor.PropertyType) is not null;
        return (value is true, Indeterminate == true || unanswered);
    }

    // RaskMarkup.Label, qualified: this type's Label property hides the chain entry of the same name.
    private HTMLLabelElement Mark(string look, string marker, UiWithField field, bool on, bool mixed)
    {
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["data-" + UiOptionLook.ControlMark] = "",
            ["data-" + marker] = "",
        };

        // Flux marks a card, a pill and a button as the field they are: label and control in one.
        if (!string.Equals(marker, "ui-checkbox", StringComparison.Ordinal))
        {
            marks["data-ui-field"] = "";
        }

        if (on && !mixed)
        {
            marks["data-checked"] = "";
        }

        if (mixed)
        {
            marks["data-indeterminate"] = "";
        }

        if (field.Invalid || _group?.Invalid == true)
        {
            marks["data-invalid"] = "";
        }

        return RaskMarkup.Label.Class(UiClass.Compose(look, Class)).Attributes(marks);
    }

    private Component Native(UiWithField field, bool on, bool mixed)
    {
        // Bind and Value are the two openings of Core's input, and both hand back an input to finish.
        if (Bind is { } bind && _group is null)
        {
            return Finish(Input.Bind(ExpressionAccessor.NonNullable(bind)).Validate(Validate).AfterBind(AfterBind).Live(), field);
        }

        return Finish(
            Input.Value(UiOptionLook.Posted(Value))
                .Type(InputType.Checkbox)
                .Checked(on && !mixed)
                .OnChange(Changed),
            field);
    }

    private HTMLInputElement<T> Finish<T>(HTMLInputElement<T> input, UiWithField field) =>
        input
            .Id(field.ControlId)
            .Name(Attributes?.ContainsKey("name") == true ? null : Name)
            .Disabled(Disabled == true || _group?.Disabled == true)
            .Aria(field.Aria)
            .Attributes(UiOptionLook.Marks(Attributes))
            .Class(UiOptionLook.Native);

    // The browser reports the box's state, not a toggle: a missed frame cannot leave the two out of step.
    private Task Changed(string state)
    {
        var on = bool.TryParse(state, out var parsed) && parsed;
        return _group is { } group ? group.Set(Value, on) : OnChange.Invoke(on).AsTask();
    }

    private object? Words() => Children?.Any() == true ? Children : Label;

    private IEnumerable<Component?> CardContent()
    {
        if (Children?.Any() == true)
        {
            return Children;
        }

        return
        [
            Div.Class(UiOptionLook.CardBody)[
                Icon is { } icon ? Ui.Icon.Name(icon).Micro.Class(UiOptionLook.CardIcon) : null,
                Div.Class("flex-1")[
                    Div.Class(UiOptionLook.CardHeading).Data("ui-heading", "")[Label],
                    Description is null ? null : Div.Class(UiOptionLook.CardSubheading).Data("ui-subheading", "")[Description]
                ]
            ],
            Ui.CheckboxIndicator.Class(UiOptionLook.InCard),
        ];
    }
}
