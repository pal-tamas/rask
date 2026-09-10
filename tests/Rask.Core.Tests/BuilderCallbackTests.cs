using System.Reflection;
using Rask.Core.DragAndDrop;

namespace Rask.Core.Tests;

// A callback property and its chain step share a name, and the property is a CARRIER — a struct
// holding the delegate rather than being one. Those two facts are one fact: a delegate-typed property
// is invocable, so C# would read `.OnSelect(fn)` as invoking it and never reach the extension setter
// (CS1593). A carrier is not invocable, so member lookup falls through and the setter binds.
//
// The receiver is still `Build<TComponent>` here, which is the other way to dodge that lookup — the
// property is not ON the receiver. Carrying the property anyway is what lets the receiver become the
// component itself, and it costs the call site nothing: `.OnSelect(Choose)` is unchanged, because the
// step has an overload per delegate shape.
internal sealed partial class BuilderCard : Component
{
    public string? Label { get; set; }
    public Callback? OnSelect { get; set; }

    protected override Component? Render() => Button.OnClick(OnSelect)[Label ?? ""];
}

internal sealed partial class CardHost : Component
{
    internal int Selected;

    protected override Component? Render() =>
        Div[BuilderCard.Label("Pick me").OnSelect(Choose)];

    internal void Choose() => Selected++;

    // Method groups off a Component, so DelegateOwner resolves an owner and AutoCallback can wrap them —
    // which is what the wrapped/raw pin below is actually measuring.
    internal void Dropped(DragDropMove move) => Selected++;

    internal void Named(string value) => Selected++;
}

public partial class BuilderCallbackTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Prop_and_setter_share_a_name() =>
        Assert.Equal("<div><button>Pick me</button></div>", CardHost.ToHtml());

    // A handler owned by a component is replaced by a re-rendering delegate, so mutating the owner's
    // state from it repaints.
    [Fact]
    public void Setter_wraps_an_owned_handler_so_it_re_renders()
    {
        var host = CardHost;
        var raw = (Action)host.Choose;

        var card = BuilderCard.OnSelect(raw);

        // Identity is asked of the DELEGATE the carrier holds, not of the carrier: a carrier is a struct
        // and two of them are equal when they hold the same handler, which is the opposite of what this
        // test wants to know. `Handler` is the wrapped-or-not delegate itself.
        Assert.NotNull(card.OnSelect);
        Assert.NotSame(raw, card.OnSelect?.Handler);
    }

    [Fact]
    public void Setter_leaves_an_unowned_handler_alone()
    {
        Action stat = Noop;

        var card = BuilderCard.OnSelect(stat);

        Assert.Same(stat, card.OnSelect?.Handler);
    }

    // A DOM handler's setter keeps the property's name — `.OnClick(…)`, not the `.Click(…)` the old
    // name-shifting rule produced for a raw delegate prop.
    [Fact]
    public void An_element_event_setter_keeps_the_On_prefix()
    {
        Action stat = Noop;

        var div = Div.OnClick(stat);

        Assert.Same(stat, div.OnClick!.Value.Handler);
    }

    // …and the argument-taking half.
    [Fact]
    public void A_typed_element_event_setter_wires_the_dom_slot()
    {
        var view = BuilderEventProbe;

        Assert.Equal(
            "<div data-rask-on-click=\"h0\" data-rask-on-mousedown=\"h1\" "
            + "data-rask-on-scroll=\"h2\"></div>",
            view.RenderAsLiveRoot());
    }

    // The hard rule nothing may quietly break: an ELEMENT handler goes straight to the DOM,
    // where handler-owner resolution already re-renders the owner — wrapping it would allocate a
    // closure per handler per render. Same owned handler as the card test above, opposite outcome.
    [Fact]
    public void An_element_event_setter_does_not_auto_wrap()
    {
        var host = CardHost;
        var raw = (Action)host.Choose;

        var div = Div.OnClick(raw);

        Assert.Same(raw, div.OnClick!.Value.Handler);
    }

    // An unset handler reads back as null. This was a real hazard while a callback was a carrier struct:
    // its implicit conversion accepted the null literal, so an omitted handler landed as a NON-null
    // carrier wrapping null and every `is not null` test a component made about its own callback flipped.
    [Fact]
    public void An_unset_element_event_reads_back_as_null()
    {
        var div = Div.OnClick(null);

        Assert.Null(div.OnClick);
        Assert.Null(div.OnMouseDown);
    }

    // The distinction that must not blur.
    // DragDrop is a plain Component, so its OnDrop stays AutoCallback-wrapped: nothing else re-renders
    // the consumer whose state the handler mutates. Input<T> is Element-derived, so its OnChange is
    // forwarded RAW to the DOM, where handler-owner resolution already re-renders and a wrapper would
    // cost a closure per handler per render. Getting either backwards is silent — the markup is
    // byte-identical either way — which is why it is pinned here rather than left to a render assertion.
    [Fact]
    public void A_component_callback_is_wrapped_where_an_element_controls_is_not()
    {
        var host = CardHost;
        var dropped = (Action<DragDropMove>)host.Dropped;
        var changed = (Action<string>)host.Named;

        Assert.NotSame(dropped, DragDrop.Body(_ => Div).OnDrop(dropped).OnDrop!.Value.Handler);

        Assert.Same(changed, Input.Of<string>().OnChange(changed).OnChange!.Value.Handler);
    }

    // A null argument reads back as null — which every `is not null` a component asks about its own
    // callback depends on (BsToast's auto-hide timer, BsDataGrid's controlled-mode gates). It took a
    // `From` helper on every assignment to hold while callbacks were carriers; now it is what assigning
    // a delegate does.
    [Fact]
    public void A_null_callback_argument_reads_back_as_unset()
    {
        Action? maybe = null;

        Assert.Null(BuilderCard.OnSelect);
        Assert.Null(BuilderCard.OnSelect(maybe).OnSelect);
    }

    // The async sibling still loses to a sync handler on the shared slot.
    // An event is ONE property over ONE slot, so there is no sync/async tiebreak left to arbitrate —
    // the last write simply wins, the way any other step does. That used to be asymmetric: a sync
    // handler beat an async one whatever the order, and RASK027 existed to flag the ambiguity. With one
    // name there is no ambiguity to flag, only a duplicated step, which RASK044 reports.
    [Fact]
    public void The_last_write_wins_the_shared_slot()
    {
        Action sync = Noop;
        // Writing the same step twice is the whole point of this test.
#pragma warning disable RASK044
        var div = Div.OnClick(() => Task.CompletedTask).OnClick(sync);
#pragma warning restore RASK044

        Assert.Same(sync, div.OnClick!.Value.Handler);
    }

    [Fact]
    public void The_last_write_wins_whichever_shape_it_is()
    {
        Func<Task> async = () => Task.CompletedTask;
#pragma warning disable RASK044
        var div = Div.OnClick(Noop).OnClick(async);
#pragma warning restore RASK044

        Assert.Same(async, div.OnClick!.Value.Handler);
    }

    // The case the old `On`-dropping rule could never reach: a delegate prop whose name does not start
    // with `On` got a setter of the same name, which the invocable-member rule could never bind to — the
    // property won and the setter was unreachable dead code. Authorize.Authorized, ErrorBoundary.Fallback
    // and DragDrop/VirtualizeModel's Body were all in that set; the chain receiver settles all of them.
    [Fact]
    public void A_non_On_delegate_prop_is_reachable_through_the_chain()
    {
        var boundary = ErrorBoundary.Fallback((ex, _) => Span[ex.Message]);

        Assert.NotNull(boundary.Fallback);
    }

    // …and an omitted one still reads back as null. `Authorize` asks exactly this about its own prop
    // ("null delegate → static authorized content via the children indexer").
    [Fact]
    public void An_omitted_non_On_delegate_prop_reads_back_as_unset()
    {
        Func<System.Security.Claims.ClaimsPrincipal, Component>? none = null;

        Assert.Null(Authorize.Authorized);
        Assert.Null(Authorize.Authorized(none).Authorized);
    }

    // A component's OWN callback prop is a CARRIER, and its setter still keeps the property's name.
    //
    // This test used to assert the opposite — `typeof(Action)`, "a callback property is a plain
    // delegate" — and that WAS true while the chain's receiver was `Build<TComponent>`: C# stops at a
    // delegate-typed property when resolving `x.OnSelect(fn)` and reads the call as an invocation
    // (CS1593), but only when the property is ON the receiver, and there it was not. Carrying the
    // property is what lets the receiver become the component itself, and it is the one cost of that:
    // a component author writes `Callback?` rather than `Action?`. The call site is unchanged —
    // `.OnSelect(Choose)` still binds, because the step has an overload per delegate shape.
    //
    // Reflection rather than "it compiles": the surface a component PRESENTS is the point, and a
    // regression here would be a property quietly reverting to a bare delegate — which compiles fine
    // today and only fails once the receiver moves.
    [Theory]
    [InlineData(typeof(BuilderCard), "OnSelect", typeof(Rask.Core.Callback))]
    public void A_callback_property_is_a_carrier(Type component, string prop, Type expected)
    {
        var p = component.GetProperty(prop, BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(p);
        Assert.Equal(expected, Nullable.GetUnderlyingType(p!.PropertyType) ?? p.PropertyType);
    }

    // The same claim on the framework's own surface. A DOM event is ONE property covering both shapes —
    // `Callback` holds a sync or an async handler — so "wire one or the other, never both" stops being a
    // rule anybody can break. Pinned by reflection because the markup is identical either way: nothing
    // would fail if these quietly split back into a pair.
    [Theory]
    [InlineData("OnClick", typeof(Rask.Core.Callback))]
    [InlineData("OnMouseDown", typeof(Rask.Core.Callback<Rask.Core.Live.MouseEventArgs>))]
    public void A_dom_event_is_one_carrier_typed_property(string prop, Type expected)
    {
        var element = typeof(Rask.Core.Element);
        var p = element.GetProperty(prop, BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(p);
        Assert.Equal(expected, Nullable.GetUnderlyingType(p!.PropertyType) ?? p.PropertyType);

        // …and the async sibling is gone, not merely unused.
        Assert.Null(element.GetProperty(prop + "Async", BindingFlags.Public | BindingFlags.Instance));
    }

    // The setter keeps the PROPERTY's name — the whole point of moving the receiver. A raw delegate prop
    // used to force `.Rate(…)` for an `OnRate` property, or no setter at all.
    [Fact]
    public void A_callback_setter_keeps_the_propertys_name()
    {
        var host = CardHost;
        var raw = (Action)host.Choose;

        var card = BuilderCard.OnSelect(raw);

        Assert.NotNull(card.OnSelect);
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
