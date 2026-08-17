namespace Rask.Html.Components;

/// <summary>
///     A generic inline container with no meaning of its own — a hook for styling or scripting a run of
///     text. The inline counterpart of <c>div</c>.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/span">MDN</see>
/// </summary>
public sealed partial class Span : Element
{
    protected override string TagName => "span";
}
