using System.ComponentModel;

namespace Rask.Core;

/// <summary>
/// An inline style written as CSS properties instead of a string:
/// <c>Div.Style(Css.Height(40.Px).Position().Sticky)</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every property browsers ship is a step, generated from MDN's data. A step takes the value as its type
/// where CSS has one (a <see cref="Length" />, a number, a duration), a keyword from the property's own set
/// (<c>Css.Display().Grid</c>), or any text CSS allows (<c>Css.Width("calc(100% - 2rem)")</c>). Text that is
/// <see langword="null" /> writes nothing, which is how a style is made conditional.
/// </para>
/// <para>
/// A style is a value: keep one in a field and build on it (<c>Sticky.Width(110.Px)</c>). It converts to the
/// text <see cref="Element.Style" /> takes, so it goes wherever a style string does.
/// </para>
/// </remarks>
public readonly partial record struct Css
{
    private readonly string? _text;

    private Css(string text) => _text = text;

    /// <summary>The style as the <c>style</c> attribute holds it, or <see langword="null" /> when it declares nothing.</summary>
    public static implicit operator string?(Css css) => css._text;

    /// <summary>The style as the <c>style</c> attribute holds it: <c>height:40px;position:sticky</c>.</summary>
    public override string ToString() => _text ?? "";

    /// <summary>This style and one more declaration. For the generated steps.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Css With(string property, string? value)
    {
        if (value is null)
        {
            return this;
        }

        var declaration = property + ":" + value;
        return new Css(_text is null ? declaration : _text + ";" + declaration);
    }
}
