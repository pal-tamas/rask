// rask-rewrite: keep the factory — this file holds BOTH surfaces on purpose and asserts they agree.
// Converting the factory half would leave a test comparing a chain to itself: still green, proving
// nothing. tools/RaskBuilderRewrite skips any file carrying this marker.

using System.Reflection;
using Rask.Core.DragAndDrop;
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

    // Method groups off a Component, so DelegateOwner resolves an owner and AutoCallback can wrap them —
    // which is what the wrapped/raw pin below is actually measuring.
    internal void Dropped(DragDropMove move) => Selected++;

    internal void Named(string value) => Selected++;
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

    // The distinction the carrier must not blur, now that every framework callback prop rides one.
    // DragDrop is a plain Component, so its OnDrop stays AutoCallback-wrapped: nothing else re-renders
    // the consumer whose state the handler mutates. Input<T> is Element-derived, so its OnChange is
    // forwarded RAW to the DOM, where handler-owner resolution already re-renders and a wrapper would
    // cost a closure per handler per render. Getting either backwards is silent — the markup is
    // byte-identical either way — so both surfaces are pinned, and so is the factory they must agree with.
    [Fact]
    public void A_component_callback_is_wrapped_where_an_element_controls_is_not()
    {
        var host = CardHost();
        var dropped = (Callback<DragDropMove>)host.Dropped;
        var changed = (Callback<string>)host.Named;

        Assert.NotSame(dropped, DragDrop(_ => Div(), OnDrop: dropped).OnDrop?.Fn);
        Assert.NotSame(dropped, DragDrop(_ => Div()).OnDrop(dropped).OnDrop?.Fn);

        Assert.Same(changed, Input<string>(OnChange: changed).OnChange?.Fn);
        Assert.Same(changed, Input<string>().OnChange(changed).OnChange?.Fn);
    }

    // A null delegate must land as an UNSET carrier on both surfaces. The implicit conversion accepts
    // one, so an argument that merely HAPPENS to be null (`OnSelect: maybe`) would convert into a
    // non-null carrier wrapping null — and every `is not null` a component asks about its own callback
    // (BsToast's auto-hide timer, BsDataGrid's controlled-mode gates) would answer true for a handler
    // nobody wired. Both surfaces assign through Handler.From, which maps null to unset.
    [Fact]
    public void A_null_callback_argument_reads_back_as_unset()
    {
        Callback? maybe = null;

        Assert.Null(BuilderCard().OnSelect);
        Assert.Null(BuilderCard(OnSelect: maybe).OnSelect);
        Assert.Null(BuilderCard().OnSelect(maybe).OnSelect);
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

    // The other half of the rule, and the one the `On` prefix never reached: a delegate prop whose
    // name does not start with `On` got a setter of the SAME name, which C#'s invocable-member rule
    // could never bind to — the property won and the setter was unreachable dead code. Rask.Core's
    // cases (Authorize.Authorized, ErrorBoundary.Fallback, DragDrop/VirtualizeModel's Body) ride a
    // carrier now, which is what makes this a setter call rather than an attempt to invoke a
    // `Func<Exception, Callback, Component>` with a `Func<Exception, Callback, Component>`.
    [Fact]
    public void A_non_On_delegate_prop_is_reachable_through_the_chain()
    {
        var boundary = ErrorBoundary().Fallback((ex, _) => Span()[ex.Message]);

        Assert.NotNull(boundary.Fallback?.Fn);
    }

    // …and an omitted one still reads back as unset. `Authorize` asks exactly this about its own prop
    // ("null delegate → static authorized content via the children indexer"), so a non-null carrier
    // wrapping null would send it down the delegate branch and NullReferenceException instead.
    [Fact]
    public void An_omitted_non_On_delegate_prop_reads_back_as_unset()
    {
        Func<System.Security.Claims.ClaimsPrincipal, Component>? none = null;

        Assert.Null(Authorize().Authorized);
        Assert.Null(Authorize(Authorized: none).Authorized);
        Assert.Null(Authorize().Authorized(none).Authorized);
    }

    // The public read surface is the CALL, not the delegate. `?.Invoke(…)` reads as what it does and is
    // null-safe by construction — on an unset carrier AND on one that somehow wraps a null delegate,
    // which is the trap `From` exists to prevent and which a hand-held delegate re-opens at every site
    // that forgets to check. So `Fn` is internal, and the carrier is declared WITHOUT a positional
    // parameter to keep it that way: a positional record publishes its parameter as a public property no
    // matter what the author intended, which is how it became public in the first place.
    //
    // Asserted by reflection rather than by "it does not compile": this test assembly has
    // InternalsVisibleTo, so `Fn` is reachable from here either way (the wrap-preservation tests above
    // use it deliberately, to compare delegate IDENTITY rather than to call it).
    [Theory]
    [InlineData(typeof(Handler), "Invoke")]
    [InlineData(typeof(HandlerAsync), "InvokeAsync")]
    [InlineData(typeof(Handler<string>), "Invoke")]
    [InlineData(typeof(HandlerAsync<string>), "InvokeAsync")]
    public void A_handler_carrier_exposes_the_call_and_not_the_delegate(Type carrier, string invoke)
    {
        Assert.Null(carrier.GetProperty("Fn", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(carrier.GetProperty("Fn", BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.NotNull(carrier.GetMethod(invoke, BindingFlags.Public | BindingFlags.Instance));

        // The positional record's other public gift: a Deconstruct that hands the delegate straight back.
        Assert.Null(carrier.GetMethod("Deconstruct", BindingFlags.Public | BindingFlags.Instance));

        // …while the two members every call site DOES need stay public: the null-preserving factory and
        // the implicit conversion that keeps `OnClick = Save` and every generated `OnClick:` working.
        Assert.NotNull(carrier.GetMethod("From", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(carrier.GetMethod("op_Implicit", BindingFlags.Public | BindingFlags.Static));
    }

    // The one carrier that keeps its delegate public, and why: `Carrier<TDelegate>` names its delegate
    // only by a type parameter, so it knows neither the arity nor the return type an `Invoke` would need
    // and no method can stand in for calling it. A component that declares a value-returning callback
    // prop (`Carrier<Func<T, string?>>? RowClass`) has to reach the delegate to use it, and it is
    // usually in another assembly — so this is a deliberate exception, not an oversight.
    [Fact]
    public void The_open_ended_carrier_keeps_its_delegate_reachable()
    {
        var carrier = typeof(Carrier<Callback>);

        Assert.NotNull(carrier.GetProperty("Fn", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(carrier.GetMethod("Deconstruct", BindingFlags.Public | BindingFlags.Instance));
    }

    // Invoke is what the carrier is FOR, so it has to behave on every shape: run the callback when one is
    // wired, no-op (and, for the async pair, hand back a completed Task) when nothing is.
    [Fact]
    public async Task Invoke_runs_a_wired_callback_and_no_ops_on_an_unset_one()
    {
        var calls = 0;
        Handler? sync = Handler.From(() => calls++);
        Handler<int>? typed = Handler<int>.From(n => calls += n);
        HandlerAsync? async = HandlerAsync.From(() =>
        {
            calls++;
            return Task.CompletedTask;
        });
        HandlerAsync<int>? typedAsync = HandlerAsync<int>.From(n =>
        {
            calls += n;
            return Task.CompletedTask;
        });

        sync?.Invoke();
        typed?.Invoke(2);
        await (async?.InvokeAsync() ?? Task.CompletedTask);
        await (typedAsync?.InvokeAsync(3) ?? Task.CompletedTask);

        Assert.Equal(7, calls);

        // The null-carrier half — `?.` never reaches Invoke…
        Handler? unset = null;
        unset?.Invoke();

        // …and the null-DELEGATE half, which is the case a raw `.Fn` call would have thrown on: the
        // implicit conversion accepts null, so a carrier wrapping nothing is constructible and Invoke
        // has to absorb it.
        Assert.Equal(7, calls);
        new Handler(null).Invoke();
        new Handler<int>(null).Invoke(1);
        await new HandlerAsync(null).InvokeAsync();
        await new HandlerAsync<int>(null).InvokeAsync(1);
        Assert.Equal(7, calls);
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
