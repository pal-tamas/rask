namespace Rask.Core.Components;

/// <summary>
///     A run of text, HTML-encoded on the way out — <c>&lt;</c> and <c>&amp;</c> arrive as literal
///     characters and are never parsed as markup. This is what a bare <c>string</c> child becomes, so
///     <c>Div()["hi"]</c> and <c>Div()[Text("hi")]</c> are the same thing. For deliberately unescaped
///     markup use <c>Raw</c>.
/// </summary>
public sealed class Text : Component
{
    public Text() { }
    public Text(string value) => Value = value;

    /// <summary>
    ///     The text to render. Encoded, so any value is safe to pass — including one that came from a user.
    /// </summary>
    public string? Value { get; set; }
}
