namespace Rask.Core.Components;

/// <summary>
///     Ruby annotations: small runs of text set alongside base text, used for East Asian pronunciation
///     guides.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/ruby">MDN</see>
/// </summary>
public sealed class Ruby : Element
{
    protected override string TagName => "ruby";
}
