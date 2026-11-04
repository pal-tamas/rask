using Rask.Core.Diagnostics;
using static Rask.Core.Tests.Components.Refusals;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

// A bound control drawn disabled takes nothing (#1271), nor does a controlled one (#1272): the browser sends no event
// for a disabled control, so a value that arrives for one was written by hand. The model keeps what it held, and Rask.Forms says so once,
// naming the field and never the value.
[Collection("ConsoleRedirect")]
public partial class DisabledBoundWritesTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task A_disabled_bound_input_refuses_what_it_is_sent()
    {
        var model = new PriceModel();
        var view = new StubComponent(() => Form.Model(model)[Input.Bind(() => model.Price).Disabled(true)]);

        var reported = await Reported(() => Change(view, "{\"value\":\"0.01\"}"));

        Assert.Equal(100m, model.Price);
        var refused = Assert.Single(reported, e => e.Category == "Rask.Forms");
        Assert.Equal(RaskLogLevel.Warning, refused.Level);
        Assert.Contains("'Price'", refused.Message, StringComparison.Ordinal);
        Assert.Contains("disabled", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("0.01", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_enabled_bound_input_still_takes_what_it_is_sent()
    {
        var model = new PriceModel();
        var view = new StubComponent(() => Form.Model(model)[Input.Bind(() => model.Price).Disabled(false)]);

        var reported = await Reported(() => Change(view, "{\"value\":\"42\"}"));

        Assert.Equal(42m, model.Price);
        Assert.DoesNotContain(reported, e => e.Category == "Rask.Forms");
    }

    [Fact]
    public async Task A_disabled_bound_checkbox_refuses_a_tick()
    {
        var model = new TermsModel();
        var view = new StubComponent(() => Form.Model(model)[Input.Bind(() => model.Agreed).Disabled(true)]);

        var reported = await Reported(() => Change(view, "{\"value\":\"true\"}"));

        Assert.False(model.Agreed);
        Assert.Single(reported, e => e.Category == "Rask.Forms" && e.Level == RaskLogLevel.Warning);
    }

    [Fact]
    public void A_disabled_live_input_wires_no_write_at_each_key()
    {
        var model = new NoteModel();
        var view = new StubComponent(() => Form.Model(model)[Input.Bind(() => model.Note).Live().Disabled(true)]);

        var html = view.RenderAsLiveRoot();

        Assert.DoesNotContain("data-rask-on-input", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_disabled_bound_textarea_refuses_what_it_is_sent()
    {
        var model = new NoteModel();
        var view = new StubComponent(() => Form.Model(model)[Textarea.Bind(() => model.Note).Disabled(true)]);

        var reported = await Reported(() => Change(view, "{\"value\":\"forged\"}"));

        Assert.Equal("kept", model.Note);
        var refused = Assert.Single(reported, e => e.Category == "Rask.Forms");
        Assert.Contains("textarea 'Note'", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("forged", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_disabled_bound_select_refuses_even_a_value_one_of_its_options_offers()
    {
        var model = new CompanyModel();
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.CompanyId).Disabled(true)[Option.Value("5"), Option.Value("10")]
        ]);

        var reported = await Reported(() => Change(view, "{\"value\":\"10\"}"));

        Assert.Equal(5, model.CompanyId);
        Assert.Single(reported, e => e.Category == "Rask.Forms" && e.Level == RaskLogLevel.Warning);
    }

    [Fact]
    public async Task A_disabled_bound_multi_select_refuses_an_empty_selection()
    {
        var model = new TagsModel();
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.Tags).Multiple(true).Disabled(true)[Option.Value("a"), Option.Value("b")]
        ]);

        var reported = await Reported(() => Change(view, "{\"value\":\"\",\"values\":[]}"));

        Assert.Equal(["a"], model.Tags);
        Assert.Single(reported, e => e.Category == "Rask.Forms" && e.Level == RaskLogLevel.Warning);
    }

    [Fact]
    public async Task A_disabled_controlled_input_does_not_hear_a_change_it_is_sent()
    {
        var heard = new List<string>();
        var view = new StubComponent(() => Input.Value("kept").Name("code").OnChange(heard.Add).Disabled(true));

        var reported = await Reported(() => Change(view, "{\"value\":\"forged\"}"));

        Assert.Empty(heard);
        var refused = Assert.Single(reported, e => e.Category == "Rask.Forms");
        Assert.Equal(RaskLogLevel.Warning, refused.Level);
        Assert.Contains("input 'code'", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("forged", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_disabled_controlled_input_does_not_hear_a_keystroke_it_is_sent()
    {
        var heard = new List<string>();
        var view = new StubComponent(() => Input.Value("kept").OnInput(heard.Add).Disabled(true));

        var reported = await Reported(() => Send(view, "input", "{\"value\":\"forged\"}"));

        Assert.Empty(heard);
        Assert.Single(reported, e => e.Category == "Rask.Forms" && e.Level == RaskLogLevel.Warning);
    }

    [Fact]
    public async Task An_enabled_controlled_input_still_hears_what_it_is_sent()
    {
        var heard = new List<string>();
        var view = new StubComponent(() => Input.Value("kept").OnChange(heard.Add).OnInput(heard.Add).Disabled(false));

        var reported = await Reported(async () =>
        {
            await Change(view, "{\"value\":\"typed\"}");
            await Send(view, "input", "{\"value\":\"typing\"}");
        });

        Assert.Equal(["typed", "typing"], heard);
        Assert.DoesNotContain(reported, e => e.Category == "Rask.Forms");
    }

    [Fact]
    public async Task A_disabled_controlled_textarea_does_not_hear_what_it_is_sent()
    {
        var heard = new List<string>();
        var view = new StubComponent(() => Textarea.Value("kept").Name("note").OnChange(heard.Add).OnInput(heard.Add).Disabled(true));

        var reported = await Reported(async () =>
        {
            await Change(view, "{\"value\":\"forged\"}");
            await Send(view, "input", "{\"value\":\"forged\"}");
        });

        Assert.Empty(heard);
        Assert.Equal(2, reported.Count(e => e.Category == "Rask.Forms" && e.Message.Contains("textarea 'note'", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task An_enabled_controlled_textarea_still_hears_what_it_is_sent()
    {
        var heard = new List<string>();
        var view = new StubComponent(() => Textarea.Value("kept").OnChange(heard.Add));

        var reported = await Reported(() => Change(view, "{\"value\":\"typed\"}"));

        Assert.Equal(["typed"], heard);
        Assert.DoesNotContain(reported, e => e.Category == "Rask.Forms");
    }

    private sealed class PriceModel
    {
        public decimal Price { get; set; } = 100m;
    }

    private sealed class TermsModel
    {
        public bool Agreed { get; set; }
    }

    private sealed class NoteModel
    {
        public string Note { get; set; } = "kept";
    }

    private sealed class CompanyModel
    {
        public int? CompanyId { get; set; } = 5;
    }

    private sealed class TagsModel
    {
        public string[] Tags { get; set; } = ["a"];
    }
}
