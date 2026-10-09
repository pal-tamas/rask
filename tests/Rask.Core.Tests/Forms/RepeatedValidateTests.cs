using Rask.Core.Forms;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

// `.Validate(a).Validate(b)`: both rules hold, in the order written, and the second is not asked about a
// value the first rejected. Before this the second step replaced the first, which nothing said.
public partial class RepeatedValidateTests : global::Rask.Core.RaskMarkup
{
    private static FieldIdentifier NameOf(Draft m) => new(m, nameof(Draft.Name));

    [Fact]
    public async Task Two_rules_run_in_the_order_written()
    {
        var m = new Draft();
        var asked = new List<string>();
        var page = Page.Render(() => Form.Model(m)[
            Input.Bind(() => m.Name)
                .Validate(_ => Ask(asked, "first"))
                .Validate(_ => Ask(asked, "second"))
        ]);

        await page.On("input").Change("Atlantis");

        Assert.Equal(["first", "second"], asked);
    }

    [Fact]
    public async Task The_second_rule_is_not_asked_about_a_value_the_first_rejected()
    {
        var m = new Draft();
        var asked = new List<string>();
        EditContext? ctx = null;
        var page = Page.Render(() => Form.Model(m)[
            Input.Bind(() => m.Name)
                .Validate(_ => Ask(asked, "first", "Name is too short."))
                .Validate(_ => Ask(asked, "second", "Name is taken.")),
            Test.EditContextProbe(c => ctx = c)
        ]);

        await page.On("input").Change("At");

        Assert.Equal(["first"], asked);
        Assert.Equal(["Name is too short."], ctx!.GetValidationMessages(NameOf(m)));
    }

    [Fact]
    public async Task The_second_rules_message_shows_when_the_first_let_the_value_through()
    {
        var m = new Draft();
        EditContext? ctx = null;
        var page = Page.Render(() => Form.Model(m)[
            Input.Bind(() => m.Name)
                .Validate(_ => [])
                .Validate(_ => ["Name is taken."]),
            Test.EditContextProbe(c => ctx = c)
        ]);

        await page.On("input").Change("Atlantis");

        Assert.Equal(["Name is taken."], ctx!.GetValidationMessages(NameOf(m)));
    }

    [Fact]
    public async Task An_awaited_rule_after_a_plain_one_runs_only_for_a_value_the_plain_one_accepts()
    {
        var m = new Draft();
        var lookups = new List<string>();
        EditContext? ctx = null;
        var page = Page.Render(() => Form.Model(m)[
            Input.Bind(() => m.Name)
                .Validate(v => v.Length < 3 ? ["Name is too short."] : [])
                .Validate(async v =>
                {
                    await Task.Yield();
                    lookups.Add(v);
                    return v == "Atlantis" ? ["Name is taken."] : [];
                }),
            Test.EditContextProbe(c => ctx = c)
        ]);

        await page.On("input").Change("At");
        var afterShort = ctx!.GetValidationMessages(NameOf(m)).ToArray();
        await page.On("input").Change("Atlantis");

        Assert.Equal(["Name is too short."], afterShort);
        Assert.Equal(["Atlantis"], lookups);
        Assert.Equal(["Name is taken."], ctx.GetValidationMessages(NameOf(m)));
    }


    [Fact]
    public async Task A_plain_rule_after_an_awaited_one_waits_for_it()
    {
        var m = new Draft();
        var asked = new List<string>();
        var page = Page.Render(() => Form.Model(m)[
            Input.Bind(() => m.Name)
                .Validate(async _ =>
                {
                    await Task.Yield();
                    return Ask(asked, "awaited");
                })
                .Validate(_ => Ask(asked, "plain"))
        ]);

        await page.On("input").Change("Atlantis");

        Assert.Equal(["awaited", "plain"], asked);
    }

    [Fact]
    public async Task The_second_awaited_rule_still_reads_the_fields_cancellation()
    {
        var m = new Draft();
        var cancellable = false;
        var page = Page.Render(() => Form.Model(m)[
            Input.Bind(() => m.Name)
                .Validate(async _ =>
                {
                    await Task.Yield();
                    return [];
                })
                .Validate(async _ =>
                {
                    cancellable = Current.Cancellation.CanBeCanceled;
                    await Task.Yield();
                    return [];
                })
        ]);

        await page.On("input").Change("Atlantis");

        Assert.True(cancellable);
    }

    [Fact]
    public async Task Three_rules_stop_at_the_first_that_rejects()
    {
        var m = new Draft();
        var asked = new List<string>();
        var page = Page.Render(() => Form.Model(m)[
            Input.Bind(() => m.Name)
                .Validate(_ => Ask(asked, "first"))
                .Validate(_ => Ask(asked, "second", "No."))
                .Validate(_ => Ask(asked, "third"))
        ]);

        await page.On("input").Change("Atlantis");

        Assert.Equal(["first", "second"], asked);
    }

    // The chain's instance is kept from one render to the next: a rule that added itself to last render's
    // rules would run twice after one re-render, and three times after two.
    [Fact]
    public async Task Rules_do_not_pile_up_from_one_render_to_the_next()
    {
        var m = new Draft();
        var asked = new List<string>();
        var page = Page.Render(() => Form.Model(m)[
            Input.Bind(() => m.Name)
                .Validate(_ => Ask(asked, "first"))
                .Validate(_ => Ask(asked, "second"))
        ]);
        await page.On("input").Change("Atlantis");
        await page.On("input").Change("Atlantis!");
        asked.Clear();

        await page.On("input").Change("Atlantis!!");

        Assert.Equal(["first", "second"], asked);
    }

    [Fact]
    public async Task A_null_rule_adds_nothing_and_takes_nothing_away()
    {
        var m = new Draft();
        var asked = new List<string>();
        Validate<string>? none = null;
        var page = Page.Render(() => Form.Model(m)[
            Input.Bind(() => m.Name).Validate(_ => Ask(asked, "first")).Validate(none)
        ]);

        await page.On("input").Change("Atlantis");

        Assert.Equal(["first"], asked);
    }

    [Fact]
    public async Task A_rule_that_hands_back_an_iterator_is_run_once()
    {
        var m = new Draft();
        var runs = 0;
        var page = Page.Render(() => Form.Model(m)[
            Input.Bind(() => m.Name).Validate(_ => Lazy()).Validate(_ => [])
        ]);

        await page.On("input").Change("Atlantis");

        Assert.Equal(1, runs);

        IEnumerable<string> Lazy()
        {
            runs++;
            yield return "No.";
        }
    }

    [Fact]
    public async Task A_select_composes_its_rules_like_every_other_control()
    {
        var m = new Draft { Name = "a" };
        var asked = new List<string>();
        var page = Page.Render(() => Form.Model(m)[
            Select.Bind(() => m.Name)
                .Validate(_ => Ask(asked, "first"))
                .Validate(_ => Ask(asked, "second"))[
                Option.Value("a")["A"],
                Option.Value("b")["B"]
            ]
        ]);

        await page.On("select").Change("b");

        Assert.Equal(["first", "second"], asked);
    }

    [Fact]
    public async Task A_forms_own_rules_compose_too()
    {
        var m = new Draft();
        var asked = new List<string>();
        var page = Page.Render(() => Form.Model(m)
            .Validate(_ => Ask(asked, "first"))
            .Validate(_ => Ask(asked, "second"))[
            Input.Bind(() => m.Name)
        ]);

        await page.On("form").Submit("""{"form":{"Name":"Atlantis"}}""");

        Assert.Equal(["first", "second"], asked);
    }

    private static IEnumerable<string> Ask(List<string> asked, string rule, string? message = null)
    {
        asked.Add(rule);
        return message is null ? [] : [message];
    }

    private sealed class Draft
    {
        public string Name { get; set; } = "";
    }
}
