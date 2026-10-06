using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Site.Features.Islands;

/// <summary>
///     A whole page rendered by <c>ReactReport.tsx</c>: the island itself owns the route, names the
///     document, and has something on screen before its chunk has loaded.
/// </summary>
/// <remarks>
///     The router builds a page, so no chain is left to take <c>.Loading(…)</c> on — an island that IS the
///     route sets it where it is constructed. One used inside another page takes it as a step:
///     <c>ReactReport.Loading(Ui.Skeleton.Class("h-40 w-full"))</c>.
/// </remarks>
[Route("islands/report")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class ReactReport : Rask.External.ReactComponent
{
    public ReactReport() => Loading = Ui.Skeleton.Class("h-40 w-full");

    /// <summary>Where the page's own link leads: the islands showcase, as the URL the host serves.</summary>
    public string Back { get; set; } = LiveOptions.PathBase + PageMeta.LinkTo(Routes.IslandsPage());

    protected override Component? HeadAssets =>
        PageMeta.For(
            "A React island as a whole page in C# — Rask",
            "A React component that owns a route of a C# app: its title set from C#, a skeleton in the "
            + "first response, and a link that navigates without a reload.",
            Routes.ReactReport());
}
