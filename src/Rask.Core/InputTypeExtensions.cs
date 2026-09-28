namespace Rask.Core;

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
