using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     One clickable region inside an image map. Only valid inside a <c>map</c>, which an <c>img</c> then
///     references through <c>UseMap</c>.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/area">MDN</see>
/// </summary>
public sealed class Area : Element
{
    protected override string TagName => "area";
    protected override bool SelfClosing => true;

    /// <summary>
    ///     The text shown in place of the region when images are unavailable. Required whenever the area
    ///     has an <c>Href</c>.
    /// </summary>
    public string? Alt { get; set; }

    /// <summary>The region's coordinates, comma-separated, interpreted according to <c>Shape</c>.</summary>
    public string? Coords { get; set; }

    /// <summary>
    ///     The region's geometry: <c>rect</c>, <c>circle</c>, <c>poly</c>, or <c>default</c> (the whole
    ///     image).
    /// </summary>
    public string? Shape { get; set; }

    /// <summary>Where the region links to. Omit it for a non-linking area.</summary>
    public string? Href { get; set; }

    /// <summary>
    ///     Which browsing context opens the link — <c>_self</c>, <c>_blank</c>, <c>_parent</c>,
    ///     <c>_top</c>, or a named frame.
    /// </summary>
    public string? Target { get; set; }

    /// <summary>
    ///     The relationship to the target, space-separated. Meaningful only alongside <c>Href</c>.
    /// </summary>
    public string? Rel { get; set; }

    /// <summary>
    ///     Downloads the target instead of navigating; a non-empty value is the suggested filename.
    /// </summary>
    public string? Download { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Alt is not null)
        {
            AppendAttr(sb, "alt", Alt);
        }

        if (Coords is not null)
        {
            AppendAttr(sb, "coords", Coords);
        }

        if (Shape is not null)
        {
            AppendAttr(sb, "shape", Shape);
        }

        if (Href is not null)
        {
            AppendUrlAttr(sb, "href", Href);
        }

        if (Target is not null)
        {
            AppendAttr(sb, "target", Target);
        }

        if (Rel is not null)
        {
            AppendAttr(sb, "rel", Rel);
        }

        if (Download is not null)
        {
            AppendAttr(sb, "download", Download);
        }
    }
}
