namespace Rask.Html.Components;

/// <summary>
///     Contact information for its nearest <c>article</c> or <c>body</c> ancestor — an author, an owner, a
///     business. Not a general-purpose element for postal addresses.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/address">MDN</see>
/// </summary>
public sealed partial class Address : Element
{
    protected override string TagName => "address";
}
