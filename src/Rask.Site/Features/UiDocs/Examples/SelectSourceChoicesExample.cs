using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectSourceChoicesExample : Component
{
    private readonly Delivery _delivery = new();

    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-3")[
            Ui.Field[
                Ui.Label["Company"],
                // In an app: .Choices(Company.Choices(c => c.Name)), the read face searching and paging in the database.
                Ui.Select.Bind(() => _delivery.CompanyId).Id("ui-select-source").Live().Choices(Companies.Source)
            ],
            Ui.Text.Data("testid", "ui-select-source-state")[
                $"Company: {_delivery.CompanyId?.ToString(CultureInfo.InvariantCulture) ?? "none"}."
            ]
        ];

    public sealed class Delivery
    {
        // Far past the first page: the source names it all the same.
        public int? CompanyId { get; set; } = 437;
    }

    // What a read face's X.Choices(…) is, over 500 companies in memory: the page of those whose name holds what was
    // typed, how many there are, and the picked rows wherever they stand.
    private sealed class Companies : IChoiceSource<int>
    {
        public static readonly IChoiceSource<int> Source = new Companies();

        private static readonly ChoiceRow<int>[] All =
            [.. Enumerable.Range(1, 500).Select(id => new ChoiceRow<int>(id, "Company " + id.ToString("000", CultureInfo.InvariantCulture)))];

        public Task<ChoiceRowPage<int>> Page(
            string typed, int skip, int take, IReadOnlyCollection<int>? picked = null, CancellationToken cancellationToken = default)
        {
            ChoiceRow<int>[] found = [.. All.Where(row => row.Text.Contains(typed, StringComparison.CurrentCultureIgnoreCase))];
            ChoiceRow<int>[] held = [.. All.Where(row => picked?.Contains(row.Id) == true)];
            return Task.FromResult(new ChoiceRowPage<int>([.. found.Skip(skip).Take(take)], found.Length, held));
        }

        public Task<ChoiceRow<int>?> Find(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(id is >= 1 and <= 500 ? All[id - 1] : (ChoiceRow<int>?)null);
    }
}
