#pragma warning disable RASK014 // test-helper subclasses have no generated factory

namespace Rask.Core.Tests.Forms;

// A read-only field cannot be changed in the browser, but its bound write was wired all the same, so a frame written
// by hand changed the model of a field the page had locked. It wires none now; its other events still arrive.
public partial class ReadOnlyBindTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_read_only_bound_input_wires_no_write()
    {
        var model = new Holder { Name = "Ada" };

        var page = Page.Render(() => Form.Model(model)[Input.Bind(() => model.Name).ReadOnly(true)]);

        Assert.Contains("readonly", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-on-change", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-on-input", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_read_only_bound_textarea_wires_no_write()
    {
        var model = new Holder { Name = "Ada" };

        var page = Page.Render(() => Form.Model(model)[Textarea.Bind(() => model.Name).ReadOnly(true)]);

        Assert.DoesNotContain("data-rask-on-change", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-on-input", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_readonly_written_through_the_attributes_bag_counts_too()
    {
        var model = new Holder { Name = "Ada" };

        var page = Page.Render(() => Form.Model(model)[
            Input.Bind(() => model.Name).Attributes(new Dictionary<string, string?> { ["readonly"] = null })]);

        Assert.DoesNotContain("data-rask-on-change", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_that_is_not_read_only_still_writes()
    {
        var model = new Holder { Name = "Ada" };

        var page = Page.Render(() => Form.Model(model)[Input.Bind(() => model.Name)]);

        Assert.Contains("data-rask-on-change", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_checkbox_which_readonly_means_nothing_to_is_left_as_it_was()
    {
        var model = new Holder();

        var page = Page.Render(() => Form.Model(model)[Input.Bind(() => model.Agreed).ReadOnly(true)]);

        Assert.Contains("data-rask-on-change", page.Html, StringComparison.Ordinal);
    }

    private sealed class Holder
    {
        public string Name { get; set; } = "";

        public bool Agreed { get; set; }
    }
}
