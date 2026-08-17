namespace Rask.Html.Components;

/// <summary>
///     A variable in a mathematical expression or a programming context — a name standing in for a value,
///     rendered italic by default. Not for emphasis (<c>em</c>) and not for literal code (<c>code</c>).
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/var">MDN</see>
/// </summary>
public sealed partial class Var : Element
{
    protected override string TagName => "var";
}
