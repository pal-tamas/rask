namespace Rask.Html.Components;

/// <summary>
///     The footer of its nearest sectioning ancestor: authorship, copyright, related links. A page may have
///     several — one per <c>article</c> or <c>section</c>.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/footer">MDN</see>
/// </summary>
public sealed partial class Footer : Element
{
    protected override string TagName => "footer";
}
