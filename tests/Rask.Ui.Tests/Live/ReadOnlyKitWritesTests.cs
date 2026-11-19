using Page = Rask.Testing.Page;

namespace Rask.UiTests.Live;

/// <summary>
///     A read-only field cannot be changed in the browser, so it wires no write: a frame sent for one was written by
///     hand and finds no handler. Core does this for a bound input or textarea; the kit does it for the phone's hidden
///     field and for an unbound input or textarea with a handler of the page's own.
/// </summary>
public sealed partial class ReadOnlyKitWritesTests : global::Rask.Core.RaskMarkup
{
    private const string Number = "+12015550123";

    [Fact]
    public async Task A_read_only_bound_phone_carries_its_number_and_takes_no_forged_write()
    {
        var model = new Contact { Mobile = Number };
        var page = Page.Render(() => Ui.Phone.Bind(() => model.Mobile).ReadOnly());

        var hidden = page.Find("input[type=\"hidden\"]");
        var writes = page.HandlerIds("change").Concat(page.HandlerIds("input")).ToList();
        foreach (var id in writes)
        {
            await page.Invoke(id, "{\"value\":\"+36301234567\"}");
        }

        Assert.Equal(Number, hidden.Attribute("value"));
        Assert.Null(hidden.Attribute("data-rask-on-change"));
        Assert.Null(hidden.Attribute("data-rask-on-input"));
        Assert.Equal(Number, model.Mobile);
    }

    [Fact]
    public void An_editable_bound_phone_still_wires_its_write()
    {
        var model = new Contact { Mobile = Number };

        var page = Page.Render(() => Ui.Phone.Bind(() => model.Mobile));

        Assert.NotNull(page.Find("input[type=\"hidden\"]").Attribute("data-rask-on-change"));
    }

    [Fact]
    public void A_read_only_input_or_textarea_wires_none_of_its_handlers_bound_or_not()
    {
        var model = new Contact { Mobile = Number };
        var heard = new List<string>();

        var pages = new[]
        {
            Page.Render(() => Ui.Input.Bind(() => model.Mobile).ReadOnly()),
            Page.Render(() => Ui.Input.Value("x").OnChange(heard.Add).OnInput(heard.Add).ReadOnly()),
            Page.Render(() => Ui.Textarea.Bind(() => model.Mobile).ReadOnly()),
            Page.Render(() => Ui.Textarea.Value("x").OnChange(heard.Add).OnInput(heard.Add).ReadOnly()),
        };

        Assert.All(pages, page =>
        {
            Assert.Empty(page.HandlerIds("change"));
            Assert.Empty(page.HandlerIds("input"));
        });
    }

    [Fact]
    public void An_editable_unbound_input_still_wires_its_handlers()
    {
        var heard = new List<string>();

        var page = Page.Render(() => Ui.Input.Value("x").OnChange(heard.Add).OnInput(heard.Add));

        Assert.NotEmpty(page.HandlerIds("change"));
        Assert.NotEmpty(page.HandlerIds("input"));
    }

    private sealed class Contact
    {
        public string? Mobile { get; set; }
    }
}
