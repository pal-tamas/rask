using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     A searched select opened without typing offers what an empty search finds, and the rows the value holds — written
///     as TA's pickers write it: a keyed select, keyed rows for both the answer and the held children, an answer that is
///     already there (2026-11-10: it opened on "Nincs találat", so a vehicle could not be picked).
/// </summary>
[Collection(AskedSearchCollection.Name)]
public partial class UiSearchOpensOnTheEmptyAnswerTests : global::Rask.Core.RaskMarkup
{
    private static readonly (string Id, string Name)[] Vehicles =
    [
        ("v1", "ABC-123"), ("v2", "BCD-234"), ("v3", "CDE-345"), ("v4", "DEF-456"),
    ];

    private sealed class Fueling
    {
        public string? VehicleId { get; set; }
    }

    private static Task<IEnumerable<Component?>> Find(string typed) =>
        Task.FromResult(Vehicles.Where(v => v.Name.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Select(v => (Component?)Ui.SelectOption.Key(v.Id).Value(v.Id)[v.Name]));

    private static IEnumerable<Component> Held(IEnumerable<string> ids) =>
        Vehicles.Where(v => ids.Contains(v.Id)).Select(v => Ui.SelectOption.Key(v.Id).Value(v.Id)[v.Name]);

    private static string[] Rows(Page page) =>
        [.. page.FindAll("[role=\"option\"]").Select(row => row.Attribute("data-rask-list-text") ?? row.TextContent.Trim())];

    [Fact]
    public void A_keyed_select_bound_to_a_held_row_opens_on_the_empty_answer()
    {
        var fueling = new Fueling { VehicleId = "v3" };

        var page = Page.Render(() =>
            Ui.Select.Key("vehicle").Bind(() => fueling.VehicleId).Listbox.Placeholder("Válasszon…").Search(Find)[Held([fueling.VehicleId!])]);

        Assert.Equal(["ABC-123", "BCD-234", "CDE-345", "DEF-456"], Rows(page));
        Assert.Equal("CDE-345", page.TextOf("[data-ui-select-button]"));
    }

    [Fact]
    public void A_keyed_multiple_listbox_with_its_chosen_rows_as_children_opens_on_the_empty_answer()
    {
        ICollection<string> chosen = ["v2", "v4"];

        var page = Page.Render(() =>
            Ui.Select.Key("vehicles").Values(chosen).Listbox.Multiple().Placeholder("Mind").OnChange(ids => chosen = ids).Search(Find)[Held(chosen)]);

        Assert.Equal(["ABC-123", "BCD-234", "CDE-345", "DEF-456"], Rows(page).Order());
    }

    [Fact]
    public void Choices_asked_with_a_held_row_open_on_the_empty_answer()
    {
        var fueling = new Fueling { VehicleId = "v3" };

        var page = Page.Render(() => Ui.Select.Key("vehicle").Bind(() => fueling.VehicleId).Choices(
            typed => Task.FromResult<IReadOnlyList<Ui.Choice<string>>>(
                [.. Vehicles.Where(v => v.Name.Contains(typed, StringComparison.OrdinalIgnoreCase)).Select(v => new Ui.Choice<string>(v.Id, v.Name))]),
            held: new Ui.Choice<string>("v3", "CDE-345")));

        Assert.Equal(["ABC-123", "BCD-234", "CDE-345", "DEF-456"], Rows(page));
    }

    [Fact]
    public void A_select_keyed_by_what_it_offers_asks_again_once_the_rows_have_loaded()
    {
        var page = Page.Render(new LoadingEditor());

        page.Shows(html => html.Contains("DEF-456", StringComparison.Ordinal));

        Assert.Equal(["ABC-123", "BCD-234", "CDE-345", "DEF-456"], Rows(page));
    }

    [Fact]
    public void A_select_whose_rows_load_after_it_mounts_offers_them_without_typing()
    {
        var page = Page.Render(new LoadingEditor { Keyed = false });

        page.Shows(html => html.Contains("DEF-456", StringComparison.Ordinal));

        Assert.Equal(["ABC-123", "BCD-234", "CDE-345", "DEF-456"], Rows(page));
    }

    [Fact]
    public void A_select_holding_nothing_whose_rows_load_after_it_mounts_offers_them_without_typing()
    {
        var page = Page.Render(new LoadingEditor { Keyed = false, VehicleId = null });

        page.Shows(html => html.Contains("DEF-456", StringComparison.Ordinal));

        Assert.Equal(["ABC-123", "BCD-234", "CDE-345", "DEF-456"], Rows(page));
    }

    [Fact]
    public void Choices_asked_of_rows_that_load_after_the_select_mounts_offer_them_without_typing()
    {
        var page = Page.Render(new LoadingEditor { Keyed = false, ByChoices = true });

        page.Shows(html => html.Contains("DEF-456", StringComparison.Ordinal));

        Assert.Equal(["ABC-123", "BCD-234", "CDE-345", "DEF-456"], Rows(page));
    }

    [Fact]
    public void A_pillbox_whose_rows_load_after_it_mounts_offers_them_without_typing()
    {
        var page = Page.Render(new LoadingPillbox());

        page.Shows(html => html.Contains("DEF-456", StringComparison.Ordinal));

        Assert.Equal(["ABC-123", "BCD-234", "CDE-345", "DEF-456"], Rows(page));
    }

    [Fact]
    public async Task A_search_that_is_the_same_one_is_not_asked_again_when_the_page_renders()
    {
        var asked = new List<string>();
        var count = 0;
        Task<IEnumerable<Component?>> Same(string typed)
        {
            asked.Add(typed);
            return Find(typed);
        }

        var page = Acting.Render(() => Fragment[Ui.Select.Key("vehicle").Value((string?)null).Search(Same), P[count.ToString(System.Globalization.CultureInfo.InvariantCulture)]]);
        count++;
        await Acting.Act(page);

        Assert.Equal([""], asked);
    }

    // A pillbox over rows read in OnMount, searched by the page.
    private sealed partial class LoadingPillbox : Component
    {
        private (string Id, string Name)[] _vehicles = [];

        public List<string> VehicleIds { get; set; } = [];

        protected override async Task OnMount()
        {
            await Task.Delay(20.Milliseconds);
            _vehicles = Vehicles;
        }

        protected override Component? Render()
        {
            var sorted = _vehicles;
            return Ui.Pillbox.Key("vehicles").Bind(() => VehicleIds)
                .Search(typed => Task.FromResult(sorted.Where(v => v.Name.Contains(typed, StringComparison.OrdinalIgnoreCase))
                    .Select(v => (Component?)Ui.PillboxOption.Key(v.Id).Value(v.Id)[v.Name])));
        }
    }

    // TA's editor: the rows are read in OnMount, so the first render has none; its picker keys the select by what it offers.
    private sealed partial class LoadingEditor : Component
    {
        private (string Id, string Name)[] _vehicles = [];

        public string? VehicleId { get; set; } = "v3";

        public bool Keyed { get; set; } = true;

        public bool ByChoices { get; set; }

        protected override async Task OnMount()
        {
            await Task.Delay(20.Milliseconds);
            _vehicles = Vehicles;
        }

        protected override Component? Render()
        {
            var sorted = _vehicles;
            var offering = Keyed ? $"vehicle:{sorted.Length}" : "vehicle";
            if (ByChoices)
            {
                return Ui.Select.Key(offering).Bind(() => VehicleId).Choices(
                    typed => Task.FromResult<IReadOnlyList<Ui.Choice<string>>>(
                        [.. sorted.Where(v => v.Name.Contains(typed, StringComparison.OrdinalIgnoreCase)).Select(v => new Ui.Choice<string>(v.Id, v.Name))]));
            }

            return Ui.Select.Key(offering).Bind(() => VehicleId).Listbox.Placeholder("Válasszon…")
                .Search(typed => Task.FromResult(sorted.Where(v => v.Name.Contains(typed, StringComparison.OrdinalIgnoreCase))
                    .Select(v => (Component?)Ui.SelectOption.Key(v.Id).Value(v.Id)[v.Name])))[
                sorted.Where(v => v.Id == VehicleId).Select(v => Ui.SelectOption.Key(v.Id).Value(v.Id)[v.Name])];
        }
    }
}
