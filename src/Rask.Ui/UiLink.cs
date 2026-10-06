using System.Text;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask;

/// <summary>
/// A link in running text.
/// </summary>
/// <remarks>
/// <para>
/// Flux UI's <c>flux:link</c>: medium weight, in the accent colour, underlined faintly until the pointer is
/// over it. <see cref="Variant" /> takes the underline away, <see cref="Accent" /> the accent.
/// </para>
/// <para>
/// Given a generated route — <c>Routes.Orders()</c> — it navigates INSIDE the app: the anchor carries
/// <c>data-rask-nav</c>, which the runtime routes without a reload, and the deploy's path base. A plain
/// string is an ordinary <c>&lt;a&gt;</c>, for a URL that leaves the app.
/// </para>
/// <para>
/// <see cref="Ui.LinkAs.Button" /> is for a link that DOES something rather than goes somewhere: a
/// <c>&lt;button type="button"&gt;</c> with the same look, taking <c>OnClick</c> and no <see cref="Href" />.
/// </para>
/// </remarks>
public sealed partial class UiLink : UiElement
{
    private static readonly IReadOnlyDictionary<string, string?> Marker = UiDataMarker.Of("ui-link");

    /// <summary>
    ///     Where it goes: a generated route (<c>Routes.Orders()</c>) to stay inside the app, or a URL string to
    ///     leave it. Sanitised as Core's <c>A</c> sanitises its own.
    /// </summary>
    public RouteUrl? Href { get; set; }

    /// <summary>How it shows that it is a link. Unset, <see cref="Ui.LinkVariant.Default" />.</summary>
    public Ui.LinkVariant? Variant { get; set; }

    /// <summary>Opens it in a new tab, with the <c>rel</c> that makes that safe.</summary>
    /// <remarks>
    ///     <c>rel="noopener noreferrer"</c> comes with <c>target="_blank"</c>: a new tab opened without it can
    ///     reach back through <c>window.opener</c>. Ignored for a generated route, which is one of your own
    ///     pages and not external by definition.
    /// </remarks>
    public bool? External { get; set; }

    /// <summary>The element it renders. Unset, <see cref="Ui.LinkAs.A" />.</summary>
    public Ui.LinkAs? As { get; set; }

    /// <summary>Whether it takes the accent colour. Unset is true; false draws it in the page's own ink.</summary>
    public bool? Accent { get; set; }

    /// <inheritdoc />
    protected override string TagName => As == Ui.LinkAs.Button ? "button" : "a";

    /// <inheritdoc />
    protected override string? ResolveClass() =>
        UiClass.Compose(
            "font-medium underline-offset-[6px]",
            Variant == Ui.LinkVariant.Subtle ? SubtleInk(Accent != false) : Ink(Accent != false),
            Variant switch
            {
                Ui.LinkVariant.Ghost => "no-underline hover:underline hover:decoration-current",
                Ui.LinkVariant.Subtle => "no-underline",
                _ => "underline hover:decoration-current",
            },
            Class);

    /// <inheritdoc />
    private protected override IReadOnlyDictionary<string, string?> ResolveData() => UiDataMarker.Join(Marker, Data);

    // The text, and an underline at a fifth of it — which is what the pointer brings up to full.
    private static string Ink(bool accent) => accent
        ? "text-fx-accent-content decoration-fx-accent-content/20"
        : "text-zinc-800 decoration-zinc-800/20 dark:text-white dark:decoration-white/20";

    private static string SubtleInk(bool accent) => accent
        ? "text-zinc-500 dark:text-white/70 hover:text-fx-accent-content"
        : "text-zinc-500 dark:text-white/70 hover:text-zinc-800 dark:hover:text-white";

    /// <inheritdoc />
    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);

        if (As == Ui.LinkAs.Button)
        {
            // Never a submit: inside a form, a button with no type submits it.
            AppendAttr(sb, "type", "button");
            return;
        }

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
