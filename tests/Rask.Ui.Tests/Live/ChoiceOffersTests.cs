using Rask.Core;
using Rask.Core.Diagnostics;
using Rask.TestSupport;

#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it

namespace Rask.UiTests.Live;

/// <summary>
///     A choice takes only what it offered (#1270, after #1268's select): a radio group's radio, in every variant,
///     takes only its own value and nothing while disabled; a checkbox of a group nothing while disabled; a disabled
///     toggle no press. The browser sends none of the rest, so a value or a press that arrives anyway was not the
///     reader's — the model keeps what it held, and <c>Rask.Forms</c> says so once, without repeating the value.
/// </summary>
[Collection(ChoiceOffersCollection.Name)]
public sealed partial class ChoiceOffersTests : global::Rask.Core.RaskMarkup
{
    public static TheoryData<Ui.RadioGroupVariant> Variants =>
    [
        Ui.RadioGroupVariant.Default,
        Ui.RadioGroupVariant.Segmented,
        Ui.RadioGroupVariant.Cards,
        Ui.RadioGroupVariant.Pills,
        Ui.RadioGroupVariant.Buttons,
    ];

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task A_radio_group_refuses_a_value_none_of_its_radios_drew(Ui.RadioGroupVariant variant)
    {
        var order = new Order();
        using var session = new OlderPage(Plans(order, variant));

        var reported = await Reported(() => session.TypedAsync(session.Ids("change")[1], "999"));

        Assert.Equal("free", order.Plan);
        Assert.Equal(0, order.AfterBinds);
        var refused = Assert.Single(reported, e => e.Category == "Rask.Forms");
        Assert.Equal(RaskLogLevel.Warning, refused.Level);
        Assert.Contains("radio 'f-plan'", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("999", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task A_radio_group_takes_the_value_its_radio_drew(Ui.RadioGroupVariant variant)
    {
        var order = new Order();
        using var session = new OlderPage(Plans(order, variant));

        var reported = await Reported(() => session.TypedAsync(session.Ids("change")[1], "pro"));

        Assert.Equal("pro", order.Plan);
        Assert.Equal(1, order.AfterBinds);
        Assert.DoesNotContain(reported, e => e.Category == "Rask.Forms");
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task A_radio_group_refuses_its_disabled_radio(Ui.RadioGroupVariant variant)
    {
        var order = new Order();
        using var session = new OlderPage(Plans(order, variant));

        var reported = await Reported(() => session.TypedAsync(session.Ids("change")[2], "team"));

        Assert.Equal("free", order.Plan);
        Assert.Equal(0, order.AfterBinds);
        Assert.Single(reported, e => e.Category == "Rask.Forms" && e.Level == RaskLogLevel.Warning);
    }

    [Fact]
    public async Task A_controlled_radio_group_does_not_hear_a_value_none_of_its_radios_drew()
    {
        var heard = new List<string>();
        using var session = new OlderPage(new StubComponent(() => Ui.RadioGroup.Value("free").OnChange(heard.Add)[
            Ui.Radio.Value("free").Label("Free"), Ui.Radio.Value("pro").Label("Pro")]));
        var pro = session.Ids("change")[1];

        await Reported(() => session.TypedAsync(pro, "999"));
        await Reported(() => session.TypedAsync(pro, "pro"));

        Assert.Equal(["pro"], heard);
    }

    [Fact]
    public async Task A_radio_valued_by_a_number_takes_the_number_as_the_render_wrote_it()
    {
        var seats = new Seats();
        using var session = new OlderPage(new StubComponent(() => Ui.RadioGroup.Bind(() => seats.Count).Live()[
            Ui.Radio.Value(5).Label("Five"), Ui.Radio.Value(10).Label("Ten")]));
        var ten = session.Ids("change")[1];

        await Reported(() => session.TypedAsync(ten, "10.0"));
        var afterForged = seats.Count;
        await Reported(() => session.TypedAsync(ten, "10"));

        Assert.Equal(5, afterForged);
        Assert.Equal(10, seats.Count);
    }

    [Fact]
    public async Task A_checkbox_group_refuses_a_tick_of_its_disabled_checkbox()
    {
        ICollection<string> picked = [];
        using var session = new OlderPage(new StubComponent(() => Ui.CheckboxGroup.Values(picked).OnChange(p => picked = p)[
            Ui.Checkbox.Value("web").Label("Web"), Ui.Checkbox.Value("ops").Label("Ops").Disabled()]));
        var ops = session.Ids("change")[1];

        var reported = await Reported(() => session.TypedAsync(ops, "true"));

        Assert.Empty(picked);
        var refused = Assert.Single(reported, e => e.Category == "Rask.Forms");
        Assert.Equal(RaskLogLevel.Warning, refused.Level);
    }

    [Fact]
    public async Task A_checkbox_group_takes_a_tick_of_a_checkbox_it_drew()
    {
        ICollection<string> picked = [];
        using var session = new OlderPage(new StubComponent(() => Ui.CheckboxGroup.Values(picked).OnChange(p => picked = p)[
            Ui.Checkbox.Value("web").Label("Web"), Ui.Checkbox.Value("ops").Label("Ops").Disabled()]));

        await Reported(() => session.TypedAsync(session.Ids("change")[0], "true"));

        Assert.Equal(["web"], picked);
    }

    [Fact]
    public async Task A_disabled_toggle_does_not_turn_when_pressed()
    {
        var on = false;
        using var session = new OlderPage(new StubComponent(() => Ui.Toggle.Checked(false).OnChange(v => on = v).Disabled()));

        await session.PressedAsync(session.Id("click"));

        Assert.False(on);
    }

    private static StubComponent Plans(Order order, Ui.RadioGroupVariant variant) =>
        new(() => Ui.RadioGroup.Bind(() => order.Plan).Live().AfterBind(_ => order.AfterBinds++).Variant(variant)[
            Ui.Radio.Value("free").Label("Free"),
            Ui.Radio.Value("pro").Label("Pro"),
            Ui.Radio.Value("team").Label("Team").Disabled()
        ]);

    private static async Task<List<RaskDiagnosticEvent>> Reported(Func<Task> act)
    {
        var captured = new List<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = captured.Add;
        try
        {
            await act();
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }

        return captured;
    }

    private sealed class Order
    {
        public string Plan { get; set; } = "free";

        public int AfterBinds { get; set; }
    }

    private sealed class Seats
    {
        public int Count { get; set; } = 5;
    }
}

/// <summary>The diagnostics sink is one for the process: what these tests read from it is theirs alone.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ChoiceOffersCollection
{
    public const string Name = "Choice offers";
}
