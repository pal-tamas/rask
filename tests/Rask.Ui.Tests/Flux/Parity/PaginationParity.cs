using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <c>fluxui.dev/components/pagination</c>, example for example.
/// </summary>
/// <remarks>
///     <para>
///     Flux's examples are each handed a Laravel paginator; here each is handed the <see cref="UiPaginator" />
///     that says the same. The page's "Scroll to top" section shows code and no rendered example, so there is
///     nothing of it to measure.
///     </para>
///     <para>
///     A preview on Flux's page is 606px wide, which is under the 640px where the page numbers appear — so
///     what is compared is the summary with Previous and Next. The numbered form is held to Flux's by
///     <c>UiPaginationTests</c>, from the same page widened.
///     </para>
/// </remarks>
public sealed partial class PaginationParity : FluxParity
{
    public override string Page => "pagination";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        // Order::paginate(): 24 results, five to a page.
        yield return ("", Preview(Ui.Pagination.Paginator(new UiPaginator { Page = 1, PerPage = 5, Total = 24 })));

        // Order::simplePaginate(): no total, and a page after this one.
        yield return ("simple-paginator", Preview(Ui.Pagination.Paginator(new UiPaginator { Page = 1, HasMore = true })));

        // Flux's demo list is not one a paginator could describe: 67 pages that step by 15 and a summary that
        // says "1 to 75". The words set the summary's width, so the docs page's words are written over ours —
        // a difference of the docs page's data, not of the component.
        var large = Ui.Pagination.Paginator(new UiPaginator { Page = 1, PerPage = 15, Total = 1000 });
        yield return ("large-result-set", Preview(Raw.Value(large.ToHtml().Replace("1 to 15 of", "1 to 75 of", StringComparison.Ordinal))));
    }

    // The width of a preview on Flux's page: the pager fills it, and reads its own width to pick its form.
    private static Component Preview(Component pager) => Div.Style("width:606px")[pager];
}
