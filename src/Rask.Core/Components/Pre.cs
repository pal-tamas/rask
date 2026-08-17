namespace Rask.Core.Components;

/// <summary>
///     Preformatted text: whitespace and line breaks are preserved exactly as written, in a monospace font
///     by default.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/pre">MDN</see>
/// </summary>
public sealed class Pre : Element
{
    protected override string TagName => "pre";
}
