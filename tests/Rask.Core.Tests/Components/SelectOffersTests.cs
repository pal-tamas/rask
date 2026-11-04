using Rask.Core.Diagnostics;
using static Rask.Core.Tests.Components.Refusals;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

// A select takes only what it offered (#1268): a value none of the options it drew sends — an id the reader was
// never shown, another tenant's — is refused, and the model keeps what it held.
[Collection("ConsoleRedirect")]
public partial class SelectOffersTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task A_bound_select_refuses_an_id_none_of_its_options_offered()
    {
        var model = new CompanyModel { CompanyId = 5 };
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.CompanyId)[Option.Value("5")["Acme"], Option.Value("10")["Globex"]]
        ]);

        var reported = await Reported(() => Change(view, "{\"value\":\"999\"}"));

        Assert.Equal(5, model.CompanyId);
        var refused = Assert.Single(reported, e => e.Category == "Rask.Forms");
        Assert.Equal(RaskLogLevel.Warning, refused.Level);
        Assert.Contains("CompanyId", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("999", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bound_select_takes_an_id_one_of_its_options_offered()
    {
        var model = new CompanyModel { CompanyId = 5 };
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.CompanyId)[Option.Value("5")["Acme"], Option.Value("10")["Globex"]]
        ]);

        var reported = await Reported(() => Change(view, "{\"value\":\"10\"}"));

        Assert.Equal(10, model.CompanyId);
        Assert.DoesNotContain(reported, e => e.Category == "Rask.Forms");
    }

    [Fact]
    public async Task A_bound_select_refuses_the_value_of_a_disabled_option()
    {
        var model = new CompanyModel { CompanyId = 5 };
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.CompanyId)[Option.Value("5"), Option.Value("7").Disabled(true)]
        ]);

        await Reported(() => Change(view, "{\"value\":\"7\"}"));

        Assert.Equal(5, model.CompanyId);
    }

    [Fact]
    public async Task A_bound_select_refuses_an_option_of_a_disabled_group_and_takes_one_of_an_enabled_group()
    {
        var model = new CompanyModel { CompanyId = 5 };
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.CompanyId)[
                Optgroup.Label("Ours")[Option.Value("5"), Option.Value("6")],
                Optgroup.Label("Closed").Disabled(true)[Option.Value("7")]
            ]
        ]);

        await Reported(() => Change(view, "{\"value\":\"7\"}"));
        var afterClosed = model.CompanyId;
        await Reported(() => Change(view, "{\"value\":\"6\"}"));

        Assert.Equal(5, afterClosed);
        Assert.Equal(6, model.CompanyId);
    }

    [Fact]
    public async Task A_nullable_select_still_clears_through_its_empty_option()
    {
        var model = new CompanyModel { CompanyId = 5 };
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.CompanyId)[Option.Value("")["None"], Option.Value("5")]
        ]);

        await Reported(() => Change(view, "{\"value\":\"\"}"));

        Assert.Null(model.CompanyId);
    }

    [Fact]
    public async Task A_select_without_an_empty_option_refuses_an_empty_value()
    {
        var model = new CompanyModel { CompanyId = 5 };
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.CompanyId)[Option.Value("5"), Option.Value("10")]
        ]);

        await Reported(() => Change(view, "{\"value\":\"\"}"));

        Assert.Equal(5, model.CompanyId);
    }

    [Fact]
    public async Task An_option_without_a_value_offers_its_text_as_the_browser_sends_it()
    {
        var model = new ColorModel { Color = null };
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.Color)[Option["  Red \n apple "], Option["Blue"]]
        ]);

        await Reported(() => Change(view, "{\"value\":\"Red apple\"}"));

        Assert.Equal("Red apple", model.Color);
    }

    [Fact]
    public async Task A_bound_enum_select_compares_the_value_in_the_spelling_the_render_wrote()
    {
        var model = new StatusModel { Status = Status.Active };
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.Status)[Option.Value("Active"), Option.Value("Inactive")]
        ]);

        await Reported(() => Change(view, "{\"value\":\"1\"}"));
        var afterNumber = model.Status;
        await Reported(() => Change(view, "{\"value\":\"Inactive\"}"));

        Assert.Equal(Status.Active, afterNumber);
        Assert.Equal(Status.Inactive, model.Status);
    }

    [Fact]
    public async Task A_bound_multi_select_drops_the_values_no_option_offered()
    {
        var model = new TagsModel();
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.Tags).Multiple(true)[Option.Value("a"), Option.Value("b")]
        ]);

        await Reported(() => Change(view, "{\"value\":\"b\",\"values\":[\"b\",\"other-tenant\"]}"));

        Assert.Equal(["b"], model.Tags);
    }

    [Fact]
    public async Task A_bound_multi_select_sent_only_values_no_option_offered_keeps_what_it_held()
    {
        var model = new TagsModel { Tags = ["a"] };
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.Tags).Multiple(true)[Option.Value("a"), Option.Value("b")]
        ]);

        var reported = await Reported(() => Change(view, "{\"value\":\"x\",\"values\":[\"x\",\"y\"]}"));

        Assert.Equal(["a"], model.Tags);
        Assert.Single(reported, e => e.Category == "Rask.Forms" && e.Level == RaskLogLevel.Warning);
    }

    [Fact]
    public async Task On_select_is_not_called_with_a_value_no_option_offered()
    {
        var heard = new List<IReadOnlyList<string>>();
        var view = new StubComponent(() =>
            Select.Value("a").OnSelect(heard.Add)[Option.Value("a"), Option.Value("b")]);

        var reported = await Reported(() => Change(view, "{\"value\":\"999\"}"));
        await Reported(() => Change(view, "{\"value\":\"b\"}"));

        Assert.Equal(["b"], Assert.Single(heard));
        var refused = Assert.Single(reported, e => e.Category == "Rask.Forms");
        Assert.Equal(RaskLogLevel.Warning, refused.Level);
        Assert.DoesNotContain("999", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task On_select_of_a_multi_select_hears_only_the_values_its_options_offered()
    {
        var heard = new List<IReadOnlyList<string>>();
        var view = new StubComponent(() =>
            Select.Value("a").Multiple(true).OnSelect(heard.Add)[
                Option.Value("a"), Option.Value("b"), Option.Value("c").Disabled(true)]);

        await Reported(() => Change(view, "{\"value\":\"b\",\"values\":[\"b\",\"c\",\"other-tenant\"]}"));

        Assert.Equal(["b"], Assert.Single(heard));
    }

    [Fact]
    public async Task On_select_written_without_the_values_is_still_not_called_for_a_value_no_option_offered()
    {
        var calls = 0;
        var view = new StubComponent(() =>
            Select.Value("a").OnSelect(() => calls++)[Option.Value("a"), Option.Value("b")]);

        await Reported(() => Change(view, "{\"value\":\"999\"}"));
        await Reported(() => Change(view, "{\"value\":\"b\"}"));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task A_controlled_select_does_not_call_its_change_with_a_value_no_option_offered()
    {
        var heard = new List<int>();
        var view = new StubComponent(() =>
            Select.Value(5).OnChange(heard.Add)[Option.Value("5"), Option.Value("10")]);

        await Reported(() => Change(view, "{\"value\":\"999\"}"));
        await Reported(() => Change(view, "{\"value\":\"10\"}"));

        Assert.Equal([10], heard);
    }

    private sealed class CompanyModel
    {
        public int? CompanyId { get; set; }
    }

    private sealed class ColorModel
    {
        public string? Color { get; set; }
    }

    private sealed class TagsModel
    {
        public string[] Tags { get; set; } = [];
    }

    private sealed class StatusModel
    {
        public Status Status { get; set; }
    }

    private enum Status { Active, Inactive }
}
