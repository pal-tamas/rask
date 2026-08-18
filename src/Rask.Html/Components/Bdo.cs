namespace Rask.Html.Components;

/// <summary>
///     Overrides the current text direction for its children, rendering them in the direction <c>Dir</c>
///     names.
///     <para>
///         <c>Dir</c> is the global attribute inherited from <see cref="Element" /> (<c>ltr</c> or
///         <c>rtl</c>), but on this element it is effectively required: a <c>bdo</c> without one does
///         nothing at all.
///     </para>
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/bdo">MDN</see>
/// </summary>
public sealed partial class Bdo : Element
{
    protected override string TagName => "bdo";
}
