using System.Text;

namespace Rask.Html.Components;

/// <summary>
///     Executable script, inline or from <c>Src</c>. Rask appends its own runtime script to the page, so
///     this is for your own additions.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/script">MDN</see>
/// </summary>
public sealed partial class Script : Element
{
    protected override string TagName => "script";

    /// <summary>
    ///     The URL of an external script. An element with <c>Src</c> must have no inline content.
    /// </summary>
    public string? Src { get; set; }

    /// <summary>
    ///     <c>module</c> for an ES module, <c>importmap</c>, or a MIME type. Omit it for a classic script.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    ///     Fetches the script in parallel and runs it as soon as it is ready, in no guaranteed order.
    ///     Ignored for inline scripts without <c>type="module"</c>.
    /// </summary>
    public bool? Async { get; set; }

    /// <summary>
    ///     Fetches in parallel but defers execution until the document has parsed, preserving document
    ///     order. Only for external scripts.
    /// </summary>
    public bool? Defer { get; set; }

    /// <summary>
    ///     The CORS mode for the fetch — <c>anonymous</c> or <c>use-credentials</c>. Needed to get full
    ///     error details from a cross-origin script.
    /// </summary>
    public string? CrossOrigin { get; set; }

    /// <summary>
    ///     A Subresource Integrity hash the fetched script must match, or the browser refuses to run it.
    /// </summary>
    public string? Integrity { get; set; }

    /// <summary>
    ///     Marks the script as a fallback to be skipped by any browser that supports ES modules.
    /// </summary>
    public bool? NoModule { get; set; }

    /// <summary>How much of the referrer to send when fetching the script.</summary>
    public string? ReferrerPolicy { get; set; }

    /// <summary>The script's encoding. Deprecated: serve UTF-8 and omit this.</summary>
    public string? Charset { get; set; }

    /// <summary>
    ///     The <c>fetchpriority</c> hint — <c>high</c>, <c>low</c>, or <c>auto</c> (the default).
    ///     <para>
    ///         The load-bearing use is <c>high</c> on the LCP image: the browser discovers it at the same
    ///         moment either way, but this moves it ahead of the other images in the queue. Marking
    ///         everything high marks nothing high.
    ///     </para>
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/script#fetchpriority">MDN</see>
    /// </summary>
    public string? FetchPriority { get; set; }

    /// <summary>
    ///     The <c>blocking</c> attribute — currently only <c>render</c>, which makes this an
    ///     opt-IN to blocking rendering until it loads, rather than the opt-out everything else is.
    ///     Reach for it when a flash of unstyled or un-scripted content is worse than the delay.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/script#blocking">MDN</see>
    /// </summary>
    public string? Blocking { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Src is not null)
        {
            AppendUrlAttr(sb, "src", Src);
        }

        if (Type is not null)
        {
            AppendAttr(sb, "type", Type);
        }

        if (Async is true)
        {
            AppendAttr(sb, "async", null);
        }

        if (Defer is true)
        {
            AppendAttr(sb, "defer", null);
        }

        if (CrossOrigin is not null)
        {
            AppendAttr(sb, "crossorigin", CrossOrigin);
        }

        if (Integrity is not null)
        {
            AppendAttr(sb, "integrity", Integrity);
        }

        if (NoModule is true)
        {
            AppendAttr(sb, "nomodule", null);
        }

        if (ReferrerPolicy is not null)
        {
            AppendAttr(sb, "referrerpolicy", ReferrerPolicy);
        }

        if (Charset is not null)
        {
            AppendAttr(sb, "charset", Charset);
        }

        if (FetchPriority is not null)
        {
            AppendAttr(sb, "fetchpriority", FetchPriority);
        }

        if (Blocking is not null)
        {
            AppendAttr(sb, "blocking", Blocking);
        }
    }
}
