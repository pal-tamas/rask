namespace Rask.Core.Components;

/// <summary>
///     Fallback parentheses around ruby text, shown only by browsers that cannot render ruby annotations.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/rp">MDN</see>
/// </summary>
public sealed class Rp : Element
{
    protected override string TagName => "rp";
}
