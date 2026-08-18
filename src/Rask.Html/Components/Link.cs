using System.Text;

namespace Rask.Html.Components;

/// <summary>
///     A relationship between this document and an external resource — most often a stylesheet, an icon, or
///     a preload hint. Rask emits its own scoped stylesheets as link elements; these are yours.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/link">MDN</see>
/// </summary>
public sealed partial class Link : Element
{
    protected override string TagName => "link";
    protected override bool SelfClosing => true;

    /// <summary>The linked resource's URL. Required.</summary>
    public string? Href { get; set; }

    /// <summary>
    ///     The relationship: <c>stylesheet</c>, <c>icon</c>, <c>preload</c>, <c>preconnect</c>,
    ///     <c>canonical</c>, <c>manifest</c>. Required.
    /// </summary>
    public string? Rel { get; set; }

    /// <summary>The resource's MIME type.</summary>
    public string? Type { get; set; }

    /// <summary>
    ///     A media query restricting when the resource applies. A non-matching stylesheet is still fetched,
    ///     at a lower priority.
    /// </summary>
    public string? Media { get; set; }

    /// <summary>The icon sizes this resource provides, for <c>Rel: "icon"</c>.</summary>
    public string? Sizes { get; set; }

    /// <summary>The linked resource's language, as a BCP 47 tag.</summary>
    public string? Hreflang { get; set; }

    /// <summary>
    ///     What kind of resource is being fetched (<c>style</c>, <c>font</c>, <c>script</c>, <c>image</c>).
    ///     Required with <c>Rel: "preload"</c> — without it the browser fetches at the wrong priority, or
    ///     twice.
    /// </summary>
    public string? As { get; set; }

    /// <summary>The CORS mode for the fetch. Fonts need <c>anonymous</c> even when same-origin.</summary>
    public string? CrossOrigin { get; set; }

    /// <summary>How much of the referrer to send when fetching the resource.</summary>
    public string? ReferrerPolicy { get; set; }

    /// <summary>For a stylesheet, prevents it being applied. Toggleable from script at runtime.</summary>
    public bool? Disabled { get; set; }

    /// <summary>The colour for a <c>mask-icon</c>. Safari-specific.</summary>
    public string? Color { get; set; }

    /// <summary>
    ///     The <c>fetchpriority</c> hint — <c>high</c>, <c>low</c>, or <c>auto</c> (the default).
    ///     <para>
    ///         The load-bearing use is <c>high</c> on the LCP image: the browser discovers it at the same
    ///         moment either way, but this moves it ahead of the other images in the queue. Marking
    ///         everything high marks nothing high.
    ///     </para>
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/link#fetchpriority">MDN</see>
    /// </summary>
    public string? FetchPriority { get; set; }

    /// <summary>
    ///     The <c>blocking</c> attribute — currently only <c>render</c>, which makes this an
    ///     opt-IN to blocking rendering until it loads, rather than the opt-out everything else is.
    ///     Reach for it when a flash of unstyled or un-scripted content is worse than the delay.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/link#blocking">MDN</see>
    /// </summary>
    public string? Blocking { get; set; }

    /// <summary>
    ///     For <c>rel="preload" as="image"</c>: the <c>srcset</c> the preload should honour. Without it a
    ///     responsive-image preload fetches the wrong candidate and the page pays for two downloads —
    ///     which is the opposite of what preloading it was for.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/link#imagesrcset">MDN</see>
    /// </summary>
    public string? ImageSrcset { get; set; }

    /// <summary>
    ///     The <c>sizes</c> that pairs with <see cref="ImageSrcset" /> on an image preload.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/link#imagesizes">MDN</see>
    /// </summary>
    public string? ImageSizes { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Href is not null)
        {
            AppendUrlAttr(sb, "href", Href);
        }

        if (Rel is not null)
        {
            AppendAttr(sb, "rel", Rel);
        }

        if (Type is not null)
        {
            AppendAttr(sb, "type", Type);
        }

        if (Media is not null)
        {
            AppendAttr(sb, "media", Media);
        }

        if (Sizes is not null)
        {
            AppendAttr(sb, "sizes", Sizes);
        }

        if (Hreflang is not null)
        {
            AppendAttr(sb, "hreflang", Hreflang);
        }

        if (As is not null)
        {
            AppendAttr(sb, "as", As);
        }

        if (CrossOrigin is not null)
        {
            AppendAttr(sb, "crossorigin", CrossOrigin);
        }

        if (ReferrerPolicy is not null)
        {
            AppendAttr(sb, "referrerpolicy", ReferrerPolicy);
        }

        if (Disabled is true)
        {
            AppendAttr(sb, "disabled", null);
        }

        if (Color is not null)
        {
            AppendAttr(sb, "color", Color);
        }

        if (FetchPriority is not null)
        {
            AppendAttr(sb, "fetchpriority", FetchPriority);
        }

        if (Blocking is not null)
        {
            AppendAttr(sb, "blocking", Blocking);
        }

        if (ImageSrcset is not null)
        {
            AppendAttr(sb, "imagesrcset", ImageSrcset);
        }

        if (ImageSizes is not null)
        {
            AppendAttr(sb, "imagesizes", ImageSizes);
        }
    }
}
