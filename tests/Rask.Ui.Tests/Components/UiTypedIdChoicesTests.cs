using System.Globalization;
using Rask.Core;
using Rask.Core.Forms;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     A select bound to a typed id (<c>CompanyId?</c>, what a generated form model holds) over a read face's
///     <c>IChoiceSource&lt;CompanyId&gt;</c>: picked and named with no bridge to the number it holds.
/// </summary>
[Collection(AskedSearchCollection.Name)]
public partial class UiTypedIdChoicesTests : global::Rask.Core.RaskMarkup
{
    private sealed class Contract
    {
        public UiTypedCompanyId? Company { get; set; }
    }

    private static string NameOf(UiTypedCompanyId id) => "Company " + id.Value.ToString("00", CultureInfo.InvariantCulture);

    [Fact]
    public void A_select_bound_to_a_typed_id_names_the_row_it_holds()
    {
        var contract = new Contract { Company = new UiTypedCompanyId(7) };

        var page = Acting.Render(() => Ui.Select.Bind(() => contract.Company).Choices(new Companies(20)));

        Assert.Equal(NameOf(new UiTypedCompanyId(7)), page.TextOf("[data-ui-select-button]"));
    }

    [Fact]
    public async Task Picking_a_row_writes_its_typed_id_to_the_member()
    {
        var contract = new Contract();
        var page = Acting.Render(() => Ui.Select.Bind(() => contract.Company).Live().Choices(new Companies(20)));

        var row = page.FindAll("[role=\"option\"]").Single(option => option.TextContent.Trim() == NameOf(new UiTypedCompanyId(5)));
        await page.On("#" + row.Attribute("id")).Click();

        Assert.Equal(new UiTypedCompanyId(5), contract.Company);
    }

    [Fact]
    public void A_bound_field_over_a_typed_id_offers_the_source()
    {
        var contract = new Contract { Company = new UiTypedCompanyId(3) };

        var page = Acting.Render(() => Ui.Field.Bind(() => contract.Company).Choices(new Companies(20)));

        Assert.Equal(NameOf(new UiTypedCompanyId(3)), page.TextOf("[data-ui-select-button]"));
    }

    [Fact]
    public void A_typed_id_goes_to_text_and_back_as_the_number_it_holds()
    {
        var id = new UiTypedCompanyId(42);

        var text = BindingHelpers.FormatValue(id);
        var parsed = BindingHelpers.TryParseValue(typeof(UiTypedCompanyId), text, out var back);

        Assert.Equal("42", text);
        Assert.True(parsed);
        Assert.Equal(id, back);
    }

    // A read face's rows in memory, keyed by the typed id.
    private sealed class Companies(int count) : IChoiceSource<UiTypedCompanyId>
    {
        private ChoiceRow<UiTypedCompanyId>[] All =>
            [.. Enumerable.Range(1, count).Select(n => new UiTypedCompanyId(n)).Select(id => new ChoiceRow<UiTypedCompanyId>(id, NameOf(id)))];

        public Task<ChoiceRowPage<UiTypedCompanyId>> Page(
            string typed, int skip, int take, IReadOnlyCollection<UiTypedCompanyId>? picked = null, CancellationToken cancellationToken = default)
        {
            var found = All.Where(row => row.Text.Contains(typed, StringComparison.OrdinalIgnoreCase)).ToArray();
            ChoiceRow<UiTypedCompanyId>[] held = [.. All.Where(row => (picked ?? []).Contains(row.Id))];
            return Task.FromResult(new ChoiceRowPage<UiTypedCompanyId>([.. found.Skip(skip).Take(take)], found.Length, held));
        }

        public Task<ChoiceRow<UiTypedCompanyId>?> Find(UiTypedCompanyId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(All.Where(row => row.Id == id).Select(static row => (ChoiceRow<UiTypedCompanyId>?)row).FirstOrDefault());
    }
}
