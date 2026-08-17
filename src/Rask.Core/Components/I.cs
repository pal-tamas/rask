namespace Rask.Core.Components;

/// <summary>
///     Text set apart from the surrounding prose without extra emphasis — a technical term, a taxonomic
///     name, a phrase in another language, a thought. For emphasis use <c>em</c>.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/i">MDN</see>
/// </summary>
public sealed class I : Element
{
    protected override string TagName => "i";
}
