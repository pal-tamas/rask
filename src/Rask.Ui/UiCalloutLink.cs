using System.Text;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask;

/// <summary>
///     Flux's <c>flux:callout.link</c>: a link in a <see cref="UiCallout" />'s text, in the text's own colour.
/// </summary>
/// <remarks>
///     <para>
///     Underlined at a fifth of the ink, which the pointer brings up to the text's colour — the underline is
///     what says it is a link, since the colour is the callout's.
///     </para>
///     <para>
///     Given a generated route (<c>Routes.Billing()</c>) it navigates inside the app, with the deploy's path
///     base on its href. A string is an ordinary link, and <see cref="External" /> opens that one in a new tab.
///     </para>
/// </remarks>
public sealed partial class UiCalloutLink : UiElement
{
    /// <summary>Where it goes: a generated route to stay inside the app, or a URL string to leave it.</summary>
    public RouteUrl? Href { get; set; }

    /// <summary>
    ///     Opens the link in a new tab, with <c>rel="noopener noreferrer"</c>. Ignored for a generated route,
    ///     which is one of the app's own pages.
    /// </summary>
    public bool? External { get; set; }

    /// <inheritdoc />
    protected override string TagName => "a";

    /// <inheritdoc />
    protected override string? ResolveClass() =>
        UiClass.Compose(
            "font-medium underline underline-offset-[6px] decoration-zinc-800/20 dark:decoration-white/20 hover:decoration-current",
            Class);

    /// <inheritdoc />
    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);

        if (Href is not { Path: not null } href)
        {
            return;
        }

        // A generated route carries its page type; a string converted to a RouteUrl does not. Only the first
        // is this app's to route, so only it is intercepted and prefixed with the deploy's PathBase (#975).
        if (href.PageType is not null)
        {
            AppendUrlAttr(sb, "href", LiveOptions.PathBase + href.ToString());
            AppendAttr(sb, "data-rask-nav", null);
            return;
        }

        AppendUrlAttr(sb, "href", href.ToString());
        if (External == true)
        {
            AppendAttr(sb, "target", "_blank");
            AppendAttr(sb, "rel", "noopener noreferrer");
        }
    }
}
