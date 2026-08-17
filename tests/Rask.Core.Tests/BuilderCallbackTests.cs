// rask-rewrite: keep the factory — this file holds BOTH surfaces on purpose and asserts they agree.
// Converting the factory half would leave a test comparing a chain to itself: still green, proving
// nothing. tools/RaskBuilderRewrite skips any file carrying this marker.

using System.Reflection;
using Rask.Core.DragAndDrop;
using static Rask.Core.Tests.Generated;

namespace Rask.Core.Tests;

// A callback property and its chain step share a name, and the property is an ORDINARY delegate.
// Those two facts are one fact: the step's receiver is `Build<TComponent>`, so C# never looks the
// property up on it and cannot read `.OnSelect(fn)` as invoking the delegate (CS1593).
//
// This file used to be the carrier's proving ground — every property here was a `Handler?` wrapping
// its delegate, purely to stay out of that lookup. What it pins now is that nothing needs to.
internal sealed partial class BuilderCard : Component
{
    public string? Label { get; set; }
    public Action? OnSelect { get; set; }

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
        Assert.Equal("<div><button>Pick me</button></div>", CardHost().ToHtml());

    // A handler owned by a component is replaced by a re-rendering delegate, exactly as the generated
    // factory does.
    [Fact]
    public void Setter_wraps_an_owned_handler_so_it_re_renders()
    {
        var host = CardHost();
        var raw = (Action)host.Choose;

        var card = BuilderCard.OnSelect(raw).Value;

        Assert.NotNull(card.OnSelect);
        Assert.NotSame(raw, card.OnSelect);
    }

    [Fact]
    public void Setter_leaves_an_unowned_handler_alone()
    {
        Action stat = Noop;

        var card = BuilderCard.OnSelect(stat).Value;

        Assert.Same(stat, card.OnSelect);
    }

    // A DOM handler's setter keeps the property's name — `.OnClick(…)`, not the `.Click(…)` the old
    // name-shifting rule produced for a raw delegate prop.
    [Fact]
    public void An_element_event_setter_keeps_the_On_prefix()
    {
        Action stat = Noop;

        var div = Div.OnClick(stat).Value;

        Assert.Same(stat, div.OnClick);
    }

    // …and the argument-taking half.
    [Fact]
    public void A_typed_element_event_setter_wires_the_dom_slot()
    {
        var view = BuilderEventProbe();

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
        var host = CardHost();
        var raw = (Action)host.Choose;

        var div = Div.OnClick(raw).Value;

        Assert.Same(raw, div.OnClick);
    }

    // An unset handler reads back as null. This was a real hazard while a callback was a carrier struct:
    // its implicit conversion accepted the null literal, so an omitted handler landed as a NON-null
    // carrier wrapping null and every `is not null` test a component made about its own callback flipped.
    [Fact]
    public void An_unset_element_event_reads_back_as_null()
    {
        var div = Div.OnClick(null).Value;

        Assert.Null(div.OnClick);
        Assert.Null(div.OnMouseDown);
    }

    // The distinction that must not blur.
    // DragDrop is a plain Component, so its OnDrop stays AutoCallback-wrapped: nothing else re-renders
    // the consumer whose state the handler mutates. Input<T> is Element-derived, so its OnChange is
    // forwarded RAW to the DOM, where handler-owner resolution already re-renders and a wrapper would
    // cost a closure per handler per render. Getting either backwards is silent — the markup is
    // byte-identical either way — so both surfaces are pinned, and so is the factory they must agree with.
    [Fact]
    public void A_component_callback_is_wrapped_where_an_element_controls_is_not()
    {
        var host = CardHost();
        var dropped = (Action<DragDropMove>)host.Dropped;
        var changed = (Action<string>)host.Named;

        Assert.NotSame(dropped, DragDrop(_ => Div(), OnDrop: dropped).OnDrop);
        Assert.NotSame(dropped, DragDrop.Body(_ => Div()).OnDrop(dropped).Value.OnDrop);

        Assert.Same(changed, Input<string>(OnChange: changed).OnChange);
        Assert.Same(changed, Input.Of<string>().OnChange(changed).Value.OnChange);
    }

    // A null argument reads back as null on both surfaces — which every `is not null` a component asks
    // about its own callback depends on (BsToast's auto-hide timer, BsDataGrid's controlled-mode gates).
    // It took a `From` helper on every assignment to hold while callbacks were carriers; now it is what
    // assigning a delegate does.
    [Fact]
    public void A_null_callback_argument_reads_back_as_unset()
    {
        Action? maybe = null;

        Assert.Null(BuilderCard().OnSelect);
        Assert.Null(BuilderCard(OnSelect: maybe).OnSelect);
        Assert.Null(BuilderCard.OnSelect(maybe).Value.OnSelect);
    }

    // The async sibling still loses to a sync handler on the shared slot.
    [Fact]
    public void The_sync_handler_still_wins_the_shared_slot()
    {
        Action sync = Noop;
        var div = Div.OnClickAsync(() => Task.CompletedTask).OnClick(sync).Value;

        Assert.Same(sync, div.OnClick);
        Assert.Null(div.OnClickAsync);
    }

    // The case the old `On`-dropping rule could never reach: a delegate prop whose name does not start
    // with `On` got a setter of the same name, which the invocable-member rule could never bind to — the
    // property won and the setter was unreachable dead code. Authorize.Authorized, ErrorBoundary.Fallback
    // and DragDrop/VirtualizeModel's Body were all in that set; the chain receiver settles all of them.
    [Fact]
    public void A_non_On_delegate_prop_is_reachable_through_the_chain()
    {
        var boundary = ErrorBoundary.Fallback((ex, _) => Span[ex.Message]).Value;

        Assert.NotNull(boundary.Fallback);
    }

    // …and an omitted one still reads back as null. `Authorize` asks exactly this about its own prop
    // ("null delegate → static authorized content via the children indexer").
    [Fact]
    public void An_omitted_non_On_delegate_prop_reads_back_as_unset()
    {
        Func<System.Security.Claims.ClaimsPrincipal, Component>? none = null;

        Assert.Null(Authorize().Authorized);
        Assert.Null(Authorize(Authorized: none).Authorized);
        Assert.Null(Authorize.Authorized(none).Value.Authorized);
    }

    // What the three carrier tests that stood here used to protect, restated as the thing that replaced
    // them. They asserted that `Handler`/`Carrier<…>` kept its delegate private behind `Invoke`, that
    // `From` mapped null to unset, and that the implicit conversion still accepted a plain delegate —
    // all of it machinery whose only job was to stop a delegate-typed property from swallowing its own
    // setter. The chain receives on `Build<TComponent>` now, so the property is not on the receiver and
    // there is nothing to hide behind: a callback property IS its delegate.
    //
    // Reflection rather than "it compiles": the surface these components present is the point, and a
    // regression here would be a carrier creeping back in rather than a compile error.
    [Theory]
    [InlineData(typeof(BuilderCard), "OnSelect", typeof(Action))]
    [InlineData(typeof(Rask.Core.Element), "OnClick", typeof(Action))]
    [InlineData(typeof(Rask.Core.Element), "OnClickAsync", typeof(Func<Task>))]
    [InlineData(typeof(Rask.Core.Element), "OnMouseDown", typeof(Action<Rask.Core.Live.MouseEventArgs>))]
    public void A_callback_property_is_a_plain_delegate(Type component, string prop, Type expected)
    {
        var p = component.GetProperty(prop, BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(p);
        Assert.Equal(expected, Nullable.GetUnderlyingType(p!.PropertyType) ?? p.PropertyType);

        // …and no carrier anywhere on the surface it belongs to.
        Assert.DoesNotContain(
            component.Assembly.GetTypes(),
            t => t.Name is "Handler" or "HandlerAsync" or "Carrier`1" or "Handler`1" or "HandlerAsync`1");
    }

    // The setter keeps the PROPERTY's name — the whole point of moving the receiver. A raw delegate prop
    // used to force `.Rate(…)` for an `OnRate` property, or no setter at all.
    [Fact]
    public void A_callback_setter_keeps_the_propertys_name()
    {
        var host = CardHost();
        var raw = (Action)host.Choose;

        var card = BuilderCard.OnSelect(raw).Value;

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
