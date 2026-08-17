using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     A hyperlink to anything a URL can address — another page, a fragment of this one, a file, an e-mail
///     address. Give it <c>Href</c> and the link text as a child; without <c>Href</c> it is only a
///     placeholder.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/a">MDN</see>
/// </summary>
public sealed class A : Element
{
    protected override string TagName => "a";

    /// <summary>
    ///     Where the link goes: a URL, a <c>#fragment</c>, or a <c>mailto:</c>/<c>tel:</c> scheme.
    /// </summary>
    public string? Href { get; set; }

    /// <summary>
    ///     Which browsing context opens the link — <c>_self</c> (default), <c>_blank</c>, <c>_parent</c>,
    ///     <c>_top</c>, or a named frame. Pair <c>_blank</c> with <c>Rel: "noopener"</c>.
    /// </summary>
    public string? Target { get; set; }

    /// <summary>
    ///     The relationship to the target, space-separated: <c>noopener</c>, <c>noreferrer</c>,
    ///     <c>nofollow</c>, <c>external</c>.
    /// </summary>
    public string? Rel { get; set; }

    /// <summary>
    ///     Downloads the target instead of navigating to it; a non-empty value is the suggested filename.
    ///     Honoured only for same-origin URLs.
    /// </summary>
    public string? Download { get; set; }

    /// <summary>
    ///     The language of the linked document, as a BCP 47 tag. A hint only — it does not change how the
    ///     link behaves.
    /// </summary>
    public string? Hreflang { get; set; }

    /// <summary>The expected MIME type of the linked resource. Advisory.</summary>
    public string? Type { get; set; }

    /// <summary>
    ///     How much of the referrer to send when following the link — e.g. <c>no-referrer</c>,
    ///     <c>origin</c>, <c>strict-origin-when-cross-origin</c>.
    /// </summary>
    public string? ReferrerPolicy { get; set; }

    /// <summary>
    ///     Space-separated URLs to notify with a POST when the link is followed. Widely blocked by privacy
    ///     settings.
    /// </summary>
    public string? Ping { get; set; }

    // OnClick / OnClickAsync are inherited from Element (the GlobalEventHandlers surface) — no longer
    // declared per-tag. The base emits data-rask-on-click in the universal handler group.

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
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

        if (Hreflang is not null)
        {
            AppendAttr(sb, "hreflang", Hreflang);
        }

        if (Type is not null)
        {
            AppendAttr(sb, "type", Type);
        }

        if (ReferrerPolicy is not null)
        {
            AppendAttr(sb, "referrerpolicy", ReferrerPolicy);
        }

        if (Ping is not null)
        {
            AppendAttr(sb, "ping", Ping);
        }
    }
}
