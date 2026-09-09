namespace Rask.Core;

/// <summary>
///     The HTML <c>input</c> type as a closed set rather than a free string — so a typo is a compile
///     error, and RASK025 can check the type against a bound <c>Input&lt;T&gt;</c>'s value type. A bound
///     input given no explicit type derives one from <c>T</c> (<c>bool</c>→<c>Checkbox</c>,
///     numeric→<c>Number</c>, <c>DateOnly</c>→<c>Date</c>, …); an explicit type overrides that.
///     <para>
///         The type is the single most consequential attribute on an input: it decides the on-screen
///         keyboard, the browser's own validation, and the autofill behaviour.
///     </para>
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/input#input_types">MDN: input types</see>
/// </summary>
public enum InputType
{
    /// <summary>A single line of free text. The default, and the fallback for any unmapped value.</summary>
    Text,

    /// <summary>A search field. Browsers may add a clear button and remember previous searches.</summary>
    Search,

    /// <summary>A telephone number. Shows a dialling keypad on mobile, and is deliberately not validated — phone-number formats vary too much.</summary>
    Tel,

    /// <summary>An absolute URL. Validated by the browser, and shows a URL-oriented keyboard.</summary>
    Url,

    /// <summary>An e-mail address, validated by the browser. With <c>Multiple</c>, a comma-separated list.</summary>
    Email,

    /// <summary>An obscured value. Only hides it on screen — it is the connection's TLS, not this, that protects it in transit.</summary>
    Password,

    /// <summary>A number, with a spinner and a numeric keyboard. Not for numeric strings that may have leading zeros or run long — a card or account number wants <c>Text</c> with an <c>InputMode</c>.</summary>
    Number,

    /// <summary>A single on/off box. Its checked state is what matters; unchecked boxes are not submitted at all.</summary>
    Checkbox,

    /// <summary>One choice from a group. Radios sharing a <c>Name</c> form the group, and one of them should start checked.</summary>
    Radio,

    /// <summary>A file picker. <c>Accept</c> filters what is offered and <c>Multiple</c> allows several — neither is a guarantee, so validate uploads on the server.</summary>
    File,

    /// <summary>A slider for an imprecise value. The exact number is invisible to the user, so pair it with an <c>output</c> if the value matters.</summary>
    Range,

    /// <summary>A colour picker, whose value is always a lower-case <c>#rrggbb</c> string.</summary>
    Color,

    /// <summary>A date with no time, as <c>yyyy-mm-dd</c>.</summary>
    Date,

    /// <summary>A date and time with no time zone. Rendered as <c>datetime-local</c> — the only multi-word HTML input type.</summary>
    DatetimeLocal,

    /// <summary>A time of day with no date.</summary>
    Time,

    /// <summary>A week and year, as <c>yyyy-Www</c>. Not supported in Firefox or Safari.</summary>
    Week,

    /// <summary>A month and year, as <c>yyyy-mm</c>. Not supported in Firefox.</summary>
    Month,

    /// <summary>A value submitted with the form but never shown. Not a security measure: the client can read and change it freely.</summary>
    Hidden,

    /// <summary>A button with no default behaviour. Prefer the <c>button</c> element, which can hold markup rather than only a value string.</summary>
    Button,

    /// <summary>A button that submits the form.</summary>
    Submit,

    /// <summary>A button that restores every control to its initial value. Rarely what a user wants — it is easy to hit by mistake.</summary>
    Reset,

    /// <summary>A graphical submit button, which also submits the click coordinates. Needs <c>Alt</c>, since it is a button.</summary>
    Image
}

/// <summary>
///     Converts an <see cref="InputType" /> to the string HTML expects.
/// </summary>
public static class InputTypeExtensions
{
    // The HTML attribute string for an InputType: each member's lower-cased name, except DatetimeLocal
    // which renders as the hyphenated "datetime-local" (the only multi-word HTML input type).

    /// <summary>
    ///     The <c>type</c> attribute value for this member — its lower-cased name, except
    ///     <see cref="InputType.DatetimeLocal" />, which renders hyphenated as <c>datetime-local</c>.
    /// </summary>
    public static string ToHtml(this InputType type) => type switch
    {
        InputType.Text => "text",
        InputType.Search => "search",
        InputType.Tel => "tel",
        InputType.Url => "url",
        InputType.Email => "email",
        InputType.Password => "password",
        InputType.Number => "number",
        InputType.Checkbox => "checkbox",
        InputType.Radio => "radio",
        InputType.File => "file",
        InputType.Range => "range",
        InputType.Color => "color",
        InputType.Date => "date",
        InputType.DatetimeLocal => "datetime-local",
        InputType.Time => "time",
        InputType.Week => "week",
        InputType.Month => "month",
        InputType.Hidden => "hidden",
        InputType.Button => "button",
        InputType.Submit => "submit",
        InputType.Reset => "reset",
        InputType.Image => "image",
        _ => "text"
    };
}
