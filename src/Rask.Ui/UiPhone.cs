using System.Linq.Expressions;
using Rask.Core.Forms;
using Rask.Core.Globalization;
using Rask.Core.Live;

namespace Rask;

/// <summary>
/// Flux UI's phone field: <c>Ui.Phone.Bind(() =&gt; m.Mobile).Country("HU").Label("Phone number")</c>.
/// </summary>
/// <remarks>
/// <para>
/// A form control over a <c>string</c>: the number in E.164 (<c>+36301234567</c>) once it is a possible one,
/// and the text as typed until then — so the bound value is not always E.164, as Flux says; validate it on
/// the server. The reader sees the number formatted as they type, in their country's own way, and picks the
/// country from a searchable list of flags; a number typed with its calling code picks it by itself.
/// </para>
/// <para>
/// All of that is intl-tel-input, the library Flux's field runs, in a file of its own that a page fetches
/// when it first draws a phone field (<c>wwwroot/js/rask-ui-phone.js</c>, written by the app's build). Typing,
/// the list and the formatting happen in the browser: the page hears the value, with the next action —
/// Flux's <c>wire:model</c> — or as it changes with <see cref="Live" />.
/// </para>
/// </remarks>
public sealed partial class UiPhone : Component, IFormControl<string>, IUiFormControl
{
    private BindTiming _timing;
    private string? _ownId;

    /// <inheritdoc cref="IUiFormControl.Label" />
    public string? Label { get; set; }

    /// <inheritdoc cref="IUiFormControl.Description" />
    public string? Description { get; set; }

    /// <inheritdoc cref="IUiFormControl.DescriptionTrailing" />
    public string? DescriptionTrailing { get; set; }

    /// <summary>The country of an empty number, by its two-letter code: <c>"HU"</c>. None when unset.</summary>
    public string? Country { get; set; }

    /// <summary>The countries the list offers, by code. Every country when unset; what can be typed is not limited.</summary>
    public IReadOnlyList<string>? Countries { get; set; }

    /// <summary>The countries the list shows first, by code; the rest follow alphabetically.</summary>
    public IReadOnlyList<string>? CountryOrder { get; set; }

    /// <summary>Shown while the number is empty.</summary>
    public string? Placeholder { get; set; }

    /// <summary>How tall it is. 40px unless this says smaller.</summary>
    public Ui.PhoneSize? Size { get; set; }

    /// <summary>Outlined, or filled where the form's other controls are.</summary>
    public Ui.PhoneVariant? Variant { get; set; }

    /// <summary>Takes no input, and the country cannot be changed.</summary>
    public bool? Disabled { get; set; }

    /// <summary>Shows its number and takes focus, but the number cannot be edited.</summary>
    public bool? ReadOnly { get; set; }

    /// <summary>Draws the error state. A bound field is invalid on its own while its form holds a message for it.</summary>
    public bool? Invalid { get; set; }

    /// <summary>The name the number posts under from a plain HTML form.</summary>
    public string? Name { get; set; }

    /// <summary>Classes for the field's root: widths and margins.</summary>
    public string? Class { get; set; }

    /// <inheritdoc cref="Element.Id" />
    public string? Id { get; set; }

    /// <inheritdoc cref="IUiFormControl.ShowValidation" />
    public bool? ShowValidation { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Value" />
    public string? Value { get; set; }

    /// <inheritdoc cref="IFormControl{T}.OnChange" />
    public Callback<string> OnChange { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Bind" />
    public Expression<Func<string>>? Bind { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Validate" />
    public Validator<string>? Validate { get; set; }

    /// <inheritdoc cref="IFormControl{T}.AfterBind" />
    public Callback<string> AfterBind { get; set; }

    /// <summary>
    ///     Writes the model as the number changes, after a 150 ms pause, and validates then:
    ///     <c>Ui.Phone.Bind(() =&gt; m.Mobile).Live()</c>. Flux's <c>wire:model.live</c>. Without it the number
    ///     is written with the next action, and travels with that — Flux's <c>wire:model</c>.
    /// </summary>
    public bool? Live
    {
        get => _timing.Live;
        set => _timing.Live = value;
    }

    /// <summary>
    ///     Writes the model once typing has paused for this long, and validates then:
    ///     <c>Ui.Phone.Bind(() =&gt; m.Mobile).Debounce(300.Milliseconds)</c>. Flux's
    ///     <c>wire:model.live.debounce.300ms</c>.
    /// </summary>
    public TimeSpan? Debounce
    {
        get => _timing.Debounce;
        set => _timing.Debounce = value;
    }

    string IUiFieldControl.ControlId => Id is null && Bind is null && Label is null
        ? _ownId ??= UiFieldId.Own(UiInstanceCounter.Next())
        : UiFieldId.Derive(Id, Bind, Label);

    string? IUiFormControl.Badge => null;

    LambdaExpression? IUiFieldControl.Bound => Bind;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var field = UiWithField.For(this);
        var id = field.ControlId;
        var size = Size ?? Ui.PhoneSize.Base;
        var variant = Variant ?? Ui.PhoneVariant.Outline;
        var country = Find(Country);

        var marks = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["data-ui-phone"] = null,
            ["data-ui-control"] = null,
            ["data-rask-phone"] = UiPhoneEngine.Href(LiveOptions.PathBase),
        };
        foreach (var (name, value) in UiMarks.Present(
            ("data-rask-phone-country", country?.Code.ToLowerInvariant()),
            ("data-rask-phone-countries", Codes(Countries)),
            ("data-rask-phone-order", Codes(CountryOrder))))
        {
            marks[name] = value;
        }

        // Flux marks the field itself invalid, and the two halves draw red from there.
        if (field.Invalid)
        {
            marks["data-invalid"] = null;
            marks["aria-invalid"] = "true";
        }

        return field.Wrap(
            Div.Class(UiClass.Compose(UiPhoneLook.Root, Class)).Attributes(marks)[
                Div.Class(UiPhoneLook.Group).Data("ui-input-group", "")[
                    UiPhonePicker.Draw(id, country, size, variant, Disabled == true),
                    Div.Class(UiPhoneLook.NumberHalf).Data("ui-input", "")[
                        Div[Number(field, size, variant)]
                    ]
                ],
                Bound()
            ]);
    }

    // The number's own input: the engine's, so it carries no value and no handler — the bound one is below.
    private HTMLInputElement<string> Number(UiWithField field, Ui.PhoneSize size, Ui.PhoneVariant variant) =>
        Input.Of<string>()
            .Id(field.ControlId)
            .Type(InputType.Tel)
            .Autocomplete("tel")
            .InputMode(InputMode.Tel)
            .Placeholder(Placeholder)
            .Disabled(Disabled == true)
            .ReadOnly(ReadOnly == true)
            .Aria(field.Aria)
            .Attributes(Marks(field.Invalid))
            .Class(UiPhoneLook.Input(size, variant));

    private static Dictionary<string, string?> Marks(bool invalid)
    {
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["data-ui-phone-input"] = null,
            ["data-ui-control"] = null,
            ["data-ui-group-target"] = null,
        };
        if (invalid)
        {
            marks["data-invalid"] = null;
        }

        return marks;
    }

    // The ONE bound field: what the engine reads off the number, written into it and announced (rask-phone.ts).
    private HTMLInputElement<string> Bound()
    {
        var input = Bind is { } bind
            ? Input.Bind(bind).Validate(Validate).AfterBind(AfterBind).Live(Live).Debounce(Debounce)
            : Input.Value(Value ?? string.Empty).OnChange(OnChange);

        return input.Type(InputType.Hidden).Name(Name).Disabled(Disabled == true);
    }

    private static UiPhoneCountry? Find(string? code) =>
        code is null ? null : Array.Find(UiPhoneCountries.All, one => string.Equals(one.Code, code, StringComparison.OrdinalIgnoreCase));

    private static string? Codes(IReadOnlyList<string>? codes) =>
        codes is null || codes.Count == 0 ? null : string.Join(',', codes).ToLowerInvariant();
}
