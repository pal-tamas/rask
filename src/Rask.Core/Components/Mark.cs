namespace Rask.Core.Components;

/// <summary>
///     Text marked for reference because it is relevant to the reader right now — search-result hits, most
///     often.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/mark">MDN</see>
/// </summary>
public sealed class Mark : Element
{
    protected override string TagName => "mark";
}
