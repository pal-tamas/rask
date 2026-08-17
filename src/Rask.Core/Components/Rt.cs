namespace Rask.Core.Components;

/// <summary>
///     The annotation of a ruby run — the pronunciation guide printed above or beside the base text.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/rt">MDN</see>
/// </summary>
public sealed class Rt : Element
{
    protected override string TagName => "rt";
}
