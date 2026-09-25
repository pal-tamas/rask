using System.Diagnostics;
using Rask.Core.Components;

namespace Rask;

/// <summary>
/// One of <see cref="Ui.IconName" />, drawn as inline SVG.
/// </summary>
/// <remarks>
/// <para>
/// The geometry is <see href="https://heroicons.com">Heroicons</see> v2 (outline), MIT-licensed, by the
/// makers of Tailwind — which is what the kit is styled with. Vendored as path data rather than taken as
/// a dependency: inlining means this package carries no icon font, no stylesheet and no static assets at
/// all, which is what lets it ship as a plain assembly.
/// </para>
/// <para>
/// Stroked in <c>currentColor</c>, so an icon takes the colour of whatever it sits in.
/// <c>aria-hidden</c> throughout: every one of these sits beside a text label, and a screen reader that
/// announced it would only repeat the label.
/// </para>
/// </remarks>
// In the root namespace beside the enum: Ui.IconName is public panel API, and Rask.Dashboard.Pages and
// .Panels both see their parent namespace without a using of their own.
public sealed partial class UiIcon : Component
{
    /// <summary>Which icon to draw.</summary>
    public required Ui.IconName Name { get; set; }

    /// <summary>
    ///     Extra classes for the call site. ADDITIVE: these are appended to the icon's own sizing
    ///     (<c>size-5 shrink-0</c>) rather than replacing it, so a caller can add a margin or a colour
    ///     without having to restate a size. Naming a size — any <c>size-*</c>, <c>w-*</c> or <c>h-*</c>
    ///     utility — suppresses the default instead of competing with it.
    /// </summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Svg.ViewBox("0 0 24 24")
            .Fill("none")
            .Stroke("currentColor")
            .StrokeWidth("1.5")
            .StrokeLinecap("round")
            .Attributes(("stroke-linejoin", "round"), ("aria-hidden", "true"), ("focusable", "false"))
            .Class(ComposeClass())[
            Shapes()
        ];

    /// <summary>
    ///     The icon's own sizing plus whatever the call site asked for.
    /// </summary>
    /// <remarks>
    ///     The default is DROPPED rather than merged when the caller names a size, because two competing
    ///     Tailwind size utilities on one element are resolved by stylesheet order, not by the order they
    ///     appear in the attribute — so merging would make the rendered size depend on how the sheet was
    ///     generated.
    ///     <para>
    ///         Applying it at all is the load-bearing part. This property used to REPLACE the sizing, which
    ///         reads as harmless until you remember an inline SVG has no intrinsic size the way a text glyph
    ///         does: a caller adding <c>me-1</c> for a margin got an icon with no width or height, which does
    ///         not render small or unstyled, it renders as nothing. Nothing catches that downstream either —
    ///         markup assertions see the class list they expected, and a browser test reports only
    ///         "element is not visible", which points at the page rather than at here.
    ///     </para>
    /// </remarks>
    private string ComposeClass()
    {
        const string ownSizing = "size-5 shrink-0";
        if (string.IsNullOrWhiteSpace(Class))
        {
            return ownSizing;
        }

        return NamesASize(Class) ? Class : ownSizing + " " + Class;
    }

    private static bool NamesASize(string classes)
    {
        foreach (var token in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            // Tailwind sizing utilities, including their variant-prefixed (`md:size-4`) and negative
            // forms. Matching the prefix is enough: nothing else in the vocabulary starts this way.
            var bare = token[(token.LastIndexOf(':') + 1)..].TrimStart('-');
            if (bare.StartsWith("size-", StringComparison.Ordinal)
                || bare.StartsWith("w-", StringComparison.Ordinal)
                || bare.StartsWith("h-", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private Component Shapes()
    {
        // Unreachable for a declared name. A new enum member without a shape would otherwise draw an empty box and
        // read as a styling fault rather than a missing case.
        var paths = UiIconPaths.For(Name) ?? throw new UnreachableException("No shape is defined for icon " + Name + ".");
        return paths.Length == 1 ? SvgPath.D(paths[0]) : Several(paths);
    }

    private static Component Several(string[] paths)
    {
        var parts = new Component[paths.Length];
        for (var i = 0; i < paths.Length; i++)
        {
            parts[i] = SvgPath.D(paths[i]);
        }

        return [.. parts];
    }
}
