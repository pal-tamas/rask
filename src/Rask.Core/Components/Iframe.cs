using System.Globalization;
using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     A nested browsing context — another document embedded in this one. Treat every third-party frame as
///     hostile: give it a <c>Sandbox</c>, and grant capabilities through <c>Allow</c> one at a time.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/iframe">MDN</see>
/// </summary>
public sealed class Iframe : Element
{
    protected override string TagName => "iframe";

    /// <summary>The URL of the document to embed.</summary>
    public string? Src { get; set; }

    /// <summary>Inline HTML for the frame, which overrides <c>Src</c>.</summary>
    public string? Srcdoc { get; set; }

    /// <summary>A name for the frame, usable as a link or form <c>target</c>.</summary>
    public string? Name { get; set; }

    /// <summary>
    ///     Restricts what the frame may do; present but empty applies every restriction. Add capabilities
    ///     back one at a time — <c>allow-scripts allow-same-origin</c> together on untrusted content lets
    ///     it remove its own sandbox.
    /// </summary>
    public string? Sandbox { get; set; }

    /// <summary>
    ///     The Permissions Policy for the frame — which powerful features (camera, geolocation, fullscreen)
    ///     it may use.
    /// </summary>
    public string? Allow { get; set; }

    /// <summary>The display width in CSS pixels.</summary>
    public int? Width { get; set; }

    /// <summary>The display height in CSS pixels.</summary>
    public int? Height { get; set; }

    /// <summary>
    ///     <c>lazy</c> defers loading the frame until it nears the viewport; <c>eager</c> is the default.
    /// </summary>
    public string? Loading { get; set; }

    /// <summary>How much of the referrer to send when fetching the framed document.</summary>
    public string? ReferrerPolicy { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Src is not null)
        {
            AppendUrlAttr(sb, "src", Src);
        }

        if (Srcdoc is not null)
        {
            AppendAttr(sb, "srcdoc", Srcdoc);
        }

        if (Name is not null)
        {
            AppendAttr(sb, "name", Name);
        }

        if (Sandbox is not null)
        {
            AppendAttr(sb, "sandbox", Sandbox);
        }

        if (Allow is not null)
        {
            AppendAttr(sb, "allow", Allow);
        }

        if (Width is not null)
        {
            AppendAttr(sb, "width", Width.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Height is not null)
        {
            AppendAttr(sb, "height", Height.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Loading is not null)
        {
            AppendAttr(sb, "loading", Loading);
        }

        if (ReferrerPolicy is not null)
        {
            AppendAttr(sb, "referrerpolicy", ReferrerPolicy);
        }
    }
}
