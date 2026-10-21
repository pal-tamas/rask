using Rask.Core;
using Rask.Site.Features;

namespace Rask.Printing.Tests.Host;

// The site's report as its own page would draw it: the kit's compiled sheet (Tailwind's preflight and every Flux
// class) and the Inter font the kit's theme names, which the host serves from its web root as the site does.
public sealed partial class SiteReportSheet : Component
{
    private const string Print =
        "body { font-family: Inter, sans-serif; margin: 0; } .break-inside-avoid { break-inside: avoid; }";

    protected override Component? Render() =>
        Div[Style[Raw.Value(UiStylesheet.Css)], Style[Raw.Value(Print)], RefundReport];
}
