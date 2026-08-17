// Template — copy to src/Rask.Html/Components/{Tag}.cs and replace {Tag}/{tag}/attrs.
// Delete the comment header. The Generated.{Tag}(...) factory is produced automatically.
namespace Rask.Html.Components;

/// <summary>The HTML <c>&lt;{tag}&gt;</c> element.</summary>
public sealed class {Tag} : Element
{
    protected override string TagName => "{tag}";

    // Void elements only (br, img, input, hr, meta, link, ...):
    // protected override bool SelfClosing => true;

    // Tag-specific attributes. Nullable => optional factory param (ergonomic for HTML attrs).
    public string? Name { get; set; }
    public bool? Open { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);          // id, class, style, data-*, ref  (order matters)
        if (Name is not null)
            AppendAttr(sb, "name", Name);
        if (Open is true)
            AppendAttr(sb, "open", null);  // bare boolean attribute
        // AppendUrlAttr(sb, "href", Href); // for URL-valued attributes
    }
}
