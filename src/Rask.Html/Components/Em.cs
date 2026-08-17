namespace Rask.Html.Components;

/// <summary>
///     Stress emphasis — the words you would lean on when reading aloud. For importance rather than
///     emphasis, use <c>strong</c>.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/em">MDN</see>
/// </summary>
public sealed partial class Em : Element
{
    protected override string TagName => "em";
}
