using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>
///     The kit's filters for a list — not a Flux component, the owner's own — each example with its code, the way it
///     behaves, and its reference.
/// </summary>
/// <remarks>
///     The examples are this page's own partials: a filter is kept in the address, so its members are the routed
///     page's <c>[QueryParam]</c>s, as they are in an app.
/// </remarks>
[Route("filters")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiFiltersPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Filters.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Filters)
            .Lead("A list's filters, each kept in the page's address. Ui.Filters draws a Filters button that opens them "
                  + "in a flyout, and Badges says which are in force. Not one of Flux UI's components: the kit's own, built "
                  + "from its select, date picker, modal and badge.")
            .Api(UiApi.Filters)[
            UiDocsExample.Key("ui-filters").Heading("Filters").File("FiltersExample.cs").Demo(FiltersExample()).Testid("ui-filters")
                .Blurb("Each control is bound to a [QueryParam] of the page, as in a form. A pick applies at once and moves "
                       + "the address, so Back undoes it and a copied link opens on the same rows. The badges say each "
                       + "filter in its control's own words, and the table's sort is no filter: Clear all leaves it."),
            UiDocsExample.Key("ui-filters-own").Heading("A filter of your own").File("FilterExample.cs").Demo(FilterExample()).Testid("ui-filters-own")
                .Blurb("Ui.Filter carries a filter the set cannot read from a control, such as a select of several "
                       + "answers: the page says what it is called, what it holds and how it is taken off."),
            UiFiltersBehaviour
        ];
}
