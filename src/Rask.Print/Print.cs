using Rask.Core;
using static Rask.Markup;

namespace Rask.Printing;

/// <summary>
///     Printing a page from the browser: parts that appear only on paper or never on paper, and the <c>@page</c> rule.
///     The browser does the rest — its print dialog offers "Save as PDF", and names the file after the page's title.
/// </summary>
/// <remarks>
///     <code>
///     Print.Styles(PrintPage.Size("A4").Landscape().Margins("15mm")),
///     Print.Hide(Nav[...]),
///     Print.Only(P["Printed on " + today]),
///     Button.Print()["Print"]
///     </code>
/// </remarks>
public static class Print
{
    internal const string OnlyClass = "rask-print-only";
    internal const string HideClass = "rask-print-hide";

    /// <summary>Content shown on paper and nowhere else.</summary>
    public static Component Only(params Component[] children) => Div.Class(OnlyClass)[children];

    /// <summary>Content shown everywhere except on paper.</summary>
    public static Component Hide(params Component[] children) => Div.Class(HideClass)[children];

    /// <summary>
    ///     The page's print stylesheet, written once where the page's markup is (it is a <c>&lt;style&gt;</c> element):
    ///     the rules <see cref="Only" /> and <see cref="Hide" /> rely on, and <paramref name="page" />'s <c>@page</c> rule.
    /// </summary>
    public static Component Styles(PrintPage page = default) =>
        Style[Raw.Value($".{OnlyClass}{{display:none}}@media print{{.{OnlyClass}{{display:block}}.{HideClass}{{display:none!important}}}}{page.Rule()}")];
}
