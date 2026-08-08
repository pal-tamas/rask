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

    protected override Component? Render() => Button.OnClick(OnSelect?.Fn)[Label ?? ""];
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

    // Element's whole event surface rides the same carriers, so a DOM handler's setter keeps the
    // property's name: `.OnClick(…)`, not the `.Click(…)` a raw delegate prop forced.
    [Fact]
    public void An_element_event_setter_keeps_the_On_prefix()
    {
        var div = Div();
        Callback stat = Noop;

        div.OnClick(stat);

        Assert.Same(stat, div.OnClick?.Fn);
    }

    // …and the argument-taking half, which rides Handler<TArgs> — the setter still takes the
    // Callback<TArgs>, because a lambda cannot reach the carrier (C# will not chain a delegate
    // conversion into a user-defined one).
    [Fact]
    public void A_typed_element_event_setter_wires_the_dom_slot()
    {
        var view = BuilderEventProbe();

        Assert.Equal(
            "<div data-rask-on-click=\"h0\" data-rask-on-mousedown=\"h1\" "
            + "data-rask-on-scroll=\"h2\"></div>",
            view.RenderAsLiveRoot());
    }

    // The hard rule the carrier must not quietly break: an ELEMENT handler goes straight to the DOM,
    // where handler-owner resolution already re-renders the owner — wrapping it would allocate a
    // closure per handler per render. Same owned handler as the card test above, opposite outcome.
    [Fact]
    public void An_element_event_setter_does_not_auto_wrap()
    {
        var host = CardHost();
        var raw = (Callback)host.Choose;

        var div = Div().OnClick(raw);

        Assert.Same(raw, div.OnClick?.Fn);
    }

    // The carrier converts FROM a delegate, and that conversion accepts the null literal too — so a
    // `cond ? handler : null` inside the property (or a factory passing its default) must not hand back
    // a carrier wrapping a null delegate. An unset handler reads back as null, the way it always did.
    [Fact]
    public void An_unset_element_event_reads_back_as_null()
    {
        var div = Div().OnClick(null);

        Assert.Null(div.OnClick);
        Assert.Null(div.OnMouseDown);
    }

    // The async sibling still loses to a sync handler on the shared slot, through the carrier.
    [Fact]
    public void The_sync_handler_still_wins_the_shared_slot()
    {
        Callback sync = Noop;
        var div = Div().OnClickAsync(() => Task.CompletedTask).OnClick(sync);

        Assert.Same(sync, div.OnClick?.Fn);
        Assert.Null(div.OnClickAsync);
    }

    private static void Noop() { }
}

// Renders through the live path so the wired slots actually emit data-rask-on-*.
internal sealed partial class BuilderEventProbe : Component
{
    protected override Component? Render() =>
        Div.OnClick(Bump).OnMouseDown(_ => Bump()).OnScroll(_ => Bump());

    private void Bump() { }
}
