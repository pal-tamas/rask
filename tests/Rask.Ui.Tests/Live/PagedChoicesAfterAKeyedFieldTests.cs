using Rask.Core;
using Rask.TestSupport;

#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it

namespace Rask.UiTests.Live;

/// <summary>
///     A form with a keyed field holding a paged picker and, after it, an unkeyed field holding another of the same type
///     (Eszaf's vehicle form: the company under <c>Ui.Field.Key("company")</c>, then the category). The second picker's
///     chain first runs on the instance position hands it — the keyed field's picker of the last render — before the row
///     claims that one back; the step it took there must not stay on it, or the second picker sees the first one's search
///     as its last, takes its own for a new one, asks it again, and every answer asks again: a socket that never fell
///     quiet, and a form too busy to say why it refused a save.
/// </summary>
public sealed partial class PagedChoicesAfterAKeyedFieldTests : global::Rask.Core.RaskMarkup
{
    private sealed class Vehicle
    {
        public int? CompanyId { get; set; }

        public int? CategoryId { get; set; }
    }

    private sealed class Store(string prefix, int count)
    {
        public int Asked { get; private set; }

        public Task<IReadOnlyList<Ui.Choice<int>>> Find(string typed, Ui.ChoicePage page)
        {
            Asked++;
            return Task.FromResult<IReadOnlyList<Ui.Choice<int>>>(
                [.. Enumerable.Range(1, count).Select(id => new Ui.Choice<int>(id, $"{prefix} {id}")).Skip(page.Skip).Take(page.Take)]);
        }
    }

    [Fact]
    public void A_paged_picker_after_a_keyed_field_with_another_is_asked_once_however_often_the_form_draws()
    {
        var vehicle = new Vehicle();
        var (companies, categories) = (new Store("Company", 80), new Store("Category", 5));
        using var session = new OlderPage(new StubComponent(() => Div[
            Ui.Field.Key("company")[Ui.Label["Company"], Ui.Select.Bind(() => vehicle.CompanyId).Choices(companies.Find)],
            Ui.Field[Ui.Label["Category"], Ui.Select.Bind(() => vehicle.CategoryId).Choices(categories.Find)]
        ]));

        session.Rerender();
        var drawn = session.Rerender();

        Assert.Equal((1, 1), (companies.Asked, categories.Asked));
        Assert.Contains("Category 5", drawn, StringComparison.Ordinal);
        Assert.Contains("Company 1", drawn, StringComparison.Ordinal);
    }
}
