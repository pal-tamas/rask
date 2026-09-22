using Rask.Core.Routing;

#pragma warning disable RASK014 // test-defined components constructed directly

namespace Rask.Testing.Tests;

// A page driven the way a person drives it — found by what they see, never by a selector.
public sealed partial class UserActionsTests : global::Rask.Core.RaskMarkup
{
    public sealed class Product
    {
        public string Name { get; set; } = "";
        public bool InStock { get; set; }
        public string Colour { get; set; } = "red";
    }

    private sealed partial class ProductForm : Component
    {
        private readonly Product _product = new();
        private string _saved = "";

        protected override Component? Render() =>
        [
            Form.Model(_product).OnValidSubmit(p => _saved = $"Saved {p.Name}, in stock {p.InStock}, {p.Colour}")[
                Label.For("name")["Name"],
                Input.Bind(() => _product.Name).Id("name"),
                Label.For("stock")["In stock"],
                Input.Bind(() => _product.InStock).Type(InputType.Checkbox).Id("stock"),
                Label.For("colour")["Colour"],
                Select.Bind(() => _product.Colour).Id("colour")[Option.Value("red")["Red"], Option.Value("green")["Green"]],
                Input.Value("").Placeholder("Search").Name("q"),
                Button.Type("submit")["Save"]
            ],
            P.Id("saved")[_saved]
        ];
    }

    private sealed partial class Loader : Component
    {
        private string _state = "Loading…";

        protected override async Task OnMount()
        {
            await Task.Delay(50);
            _state = "Loaded";
        }

        protected override Component? Render() => P[_state];
    }

    private sealed partial class Groceries : Component
    {
        private string _deleted = "nothing";

        protected override Component? Render() =>
        [
            Ul[
                Li["Tea ", Button.OnClick(() => _deleted = "Tea")["Delete"]],
                Li["Coffee ", Button.OnClick(() => _deleted = "Coffee")["Delete"]]
            ],
            P[$"Deleted {_deleted}"]
        ];
    }

    [Fact]
    public async Task Typing_into_a_labelled_field_and_submitting_saves_what_was_typed()
    {
        var page = Test.Render(new ProductForm());

        await page.Type("Tea").Into("Name");
        await page.Click("Save");

        page.Shows("Saved Tea, in stock False, red");
    }

    [Fact]
    public async Task Ticking_a_box_and_picking_an_option_are_both_saved()
    {
        var page = Test.Render(new ProductForm());

        await page.Check("In stock");
        await page.Pick("Green").From("Colour");
        await page.Click("Save");

        page.Shows("in stock True, green");
    }

    [Fact]
    public async Task A_field_can_be_found_by_its_placeholder()
    {
        var page = Test.Render(new ProductForm());

        var failure = await Record.ExceptionAsync(() => page.Type("tea").Into("Search"));

        // Found, and refused for the honest reason: nothing listens to it.
        Assert.Contains("has no input or change handler", failure?.Message);
    }

    [Fact]
    public async Task A_field_nobody_labelled_so_fails_listing_the_fields_there_are()
    {
        var page = Test.Render(new ProductForm());

        var failure = await Assert.ThrowsAsync<PageException>(() => page.Type("Tea").Into("Title"));

        Assert.Contains("No field is labelled \"Title\"", failure.Message);
        Assert.Contains("Search", failure.Message);
    }

    [Fact]
    public void Shows_waits_for_async_work_to_land()
    {
        var page = Test.Render(new Loader());

        page.Shows("Loaded");

        page.DoesNotShow("Loading…");
    }

    [Fact]
    public void Shows_fails_saying_what_the_page_shows_instead()
    {
        var page = Test.Render(new Loader());
        page.Patience = TimeSpan.FromMilliseconds(200);

        var failure = Assert.Throws<PageException>(() => page.Shows("Product saved"));

        Assert.Contains("Expected the page to show \"Product saved\"", failure.Message);
        Assert.Contains("Loaded", failure.Message);
    }

    [Fact]
    public async Task An_ambiguous_click_asks_for_a_region_and_In_picks_the_one_meant()
    {
        var page = Test.Render(new Groceries());

        var ambiguous = await Assert.ThrowsAsync<PageException>(async () => await page.Click("Delete"));
        await page.Click("Delete").In("Coffee");

        Assert.Contains("found 2", ambiguous.Message);
        page.Shows("Deleted Coffee");
    }

    [Fact]
    public void Shows_In_narrows_the_check_to_one_region()
    {
        var page = Test.Render(new Groceries());

        page.Shows("Tea").In("Tea");

        Assert.Throws<PageException>(() => page.Shows("Tea").In("Coffee"));
    }
}
