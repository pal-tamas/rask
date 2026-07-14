namespace Rask.Core;

// The HTML <input> type, as a closed set instead of a free string — so the type is validated at compile
// time and the analyzer (RASK025) can check it against a bound Input<T>'s value type. When an Input<T> is
// bound and no Type is given, the type is derived from T (bool→Checkbox, numeric→Number, DateOnly→Date, …);
// an explicit Type overrides that.
public enum InputType
{
    Text,
    Search,
    Tel,
    Url,
    Email,
    Password,
    Number,
    Checkbox,
    Radio,
    File,
    Range,
    Color,
    Date,

    // Rendered as "datetime-local" (the only multi-word HTML input type).
    DatetimeLocal,
    Time,
    Week,
    Month,
    Hidden,
    Button,
    Submit,
    Reset,
    Image
}

public static class InputTypeExtensions
{
    // The HTML attribute string for an InputType: each member's lower-cased name, except DatetimeLocal
    // which renders as the hyphenated "datetime-local" (the only multi-word HTML input type).
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
