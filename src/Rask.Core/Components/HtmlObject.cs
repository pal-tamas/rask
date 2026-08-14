using System.Globalization;
using System.Text;

namespace Rask.Core.Components;

// Renders the <object> HTML tag. Renamed from Object to avoid shadowing System.Object.

/// <summary>
///     External content handled by a browser plugin or built-in viewer — a PDF, an image, a nested
///     document. Named <c>HtmlObject</c> because <c>Object</c> is taken; it still renders as <c>object</c>.
///     Prefer <c>img</c>, <c>video</c> or <c>iframe</c> where they fit.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/object">MDN</see>
/// </summary>
public sealed class HtmlObject : Element
{
    protected override string TagName => "object";

    /// <summary>
    ///     The URL of the resource, rendered as the <c>data</c> attribute. Named to avoid colliding with
    ///     the universal <c>Data</c> dictionary that emits <c>data-*</c>.
    /// </summary>
    public string? DataUrl { get; set; }

    /// <summary>The resource's MIME type.</summary>
    public string? Type { get; set; }

    /// <summary>A name for the nested browsing context.</summary>
    public string? Name { get; set; }

    /// <summary>The display width in CSS pixels.</summary>
    public int? Width { get; set; }

    /// <summary>The display height in CSS pixels.</summary>
    public int? Height { get; set; }

    /// <summary>The <c>id</c> of the form this object belongs to.</summary>
    public new string? Form { get; set; }

    /// <summary>The <c>#name</c> of a <c>map</c> to apply to the object.</summary>
    public string? UseMap { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (DataUrl is not null)
        {
            AppendUrlAttr(sb, "data", DataUrl);
        }

        if (Type is not null)
        {
            AppendAttr(sb, "type", Type);
        }

        if (Name is not null)
        {
            AppendAttr(sb, "name", Name);
        }

        if (Width is not null)
        {
            AppendAttr(sb, "width", Width.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Height is not null)
        {
            AppendAttr(sb, "height", Height.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Form is not null)
        {
            AppendAttr(sb, "form", Form);
        }

        if (UseMap is not null)
        {
            AppendAttr(sb, "usemap", UseMap);
        }
    }
}
