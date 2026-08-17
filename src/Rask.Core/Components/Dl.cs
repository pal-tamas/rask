namespace Rask.Core.Components;

/// <summary>
///     A description list: <c>dt</c> terms each followed by their <c>dd</c> descriptions. Good for metadata
///     and glossaries.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/dl">MDN</see>
/// </summary>
public sealed class Dl : Element
{
    protected override string TagName => "dl";
}
