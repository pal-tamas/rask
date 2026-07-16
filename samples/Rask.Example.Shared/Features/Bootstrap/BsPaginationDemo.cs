namespace Rask.Example.Shared.Features;

// A Bootstrap pagination control driven by Rask's live runtime — no bootstrap.js. Each BsPageItem is a
// real <button>; its OnClick flips _page and the live diff re-renders, moving the .active marker and
// disabling Previous/Next at the ends. The readout below tracks the current page.
public sealed class BsPaginationDemo : Component
{
    private const int TotalPages = 5;
    private int _page = 1;

    protected override Component? Render()
    {
        IEnumerable<Component?> items =
        [
            BsPageItem(Key: "prev", Disabled: _page == 1, OnClick: () => { if (_page > 1) _page--; })["Previous"],
            .. Enumerable.Range(1, TotalPages)
                .Select(p => BsPageItem(Key: p.ToString(), Active: p == _page, OnClick: () => _page = p)[p.ToString()]),
            BsPageItem(Key: "next", Disabled: _page == TotalPages, OnClick: () => { if (_page < TotalPages) _page++; })["Next"],
        ];

        return
        [
            BsPagination(Label: "Demo pages")[items],
            P(Id: "bs-pagination-status", Class: "mt-2 mb-0 text-body-secondary")[$"Page {_page} of {TotalPages}"]
        ];
    }
}
