using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PillboxPagedChoicesExample : Component
{
    private readonly Basket _basket = new();

    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-3")[
            Ui.Field[
                Ui.Label["Products"],
                // 500 products: the list asks for a page at a time as it is scrolled, and the search starts again from the first.
                Ui.Pillbox.Bind(() => _basket.ProductIds).Id("ui-pillbox-paged").Live().Choices(Products)
            ],
            Ui.Text.Data("testid", "ui-pillbox-paged-state")[$"Products: {string.Join(", ", _basket.ProductIds)}."]
        ];

    // The store's answer: one page of the products whose name holds what was typed, in a fixed order so the pages follow on.
    private static Task<IReadOnlyList<Ui.Choice<int>>> Products(string typed, Ui.ChoicePage page) =>
        Task.FromResult<IReadOnlyList<Ui.Choice<int>>>(
            [
                .. Enumerable.Range(1, 500)
                    .Select(id => new Ui.Choice<int>(id, "Product " + id.ToString("000", CultureInfo.InvariantCulture)))
                    .Where(product => product.Name.Contains(typed, StringComparison.CurrentCultureIgnoreCase))
                    .Skip(page.Skip).Take(page.Take),
            ]);

    public sealed class Basket
    {
        public ICollection<int> ProductIds { get; set; } = [];
    }
}
