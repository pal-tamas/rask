namespace Rask.Core.Components;

/// <summary>
///     Content tangential to what surrounds it: a pull quote, a glossary note, an advertisement, a group of
///     nav links to related reading.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/aside">MDN</see>
/// </summary>
public sealed class Aside : Element
{
    protected override string TagName => "aside";
}
