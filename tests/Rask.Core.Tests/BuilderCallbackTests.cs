using static Rask.Core.Tests.Generated;

namespace Rask.Core.Tests;

// PROTOTYPE — the point of Handler: a callback prop and its builder setter share a name.
// With a raw `Callback?` prop this file would not compile (CS1593: the delegate wins).
//
// Note `Handler?`, not `Handler`. A non-nullable struct with no initializer is a *required*
// factory parameter (RASK001), so declaring the carrier non-nullable would silently turn every
// optional callback into a required argument.
internal sealed partial class BuilderCard : Component
{
    public new string? Label { get; set; }
    public Handler? OnSelect { get; set; }

    protected override Component? Render() => Button.Click(OnSelect?.Fn)[Label ?? ""];
}

internal sealed partial class CardHost : Component
{
    internal int Selected;

    protected override Component? Render() =>
        Div[BuilderCard().Label("Pick me").OnSelect(Choose)];

    internal void Choose() => Selected++;
}

public class BuilderCallbackTests
{
    [Fact]
    public void Prop_and_setter_share_a_name() =>
        Assert.Equal("<div><button>Pick me</button></div>", CardHost().ToHtml());

    [Fact]
    public void Assignment_still_works_through_the_implicit_conversion()
    {
        Callback h = () => { };
        Handler carried = h;
        Assert.Same(h, carried.Fn);
    }

    // The wrap must survive the carrier: a handler owned by a component is replaced by a
    // re-rendering delegate, exactly as the generated factory does today.
    [Fact]
    public void Setter_wraps_an_owned_handler_so_it_re_renders()
    {
        var host = CardHost();
        var card = BuilderCard();
        var raw = (Callback)host.Choose;

        card.OnSelect(raw);

        Assert.NotNull(card.OnSelect?.Fn);
        Assert.NotSame(raw, card.OnSelect?.Fn);
    }

    [Fact]
    public void Setter_leaves_an_unowned_handler_alone()
    {
        var card = BuilderCard();
        Callback stat = Noop;

        card.OnSelect(stat);

        Assert.Same(stat, card.OnSelect?.Fn);
    }

    private static void Noop() { }
}
