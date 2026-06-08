using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Contexts;

public class ContextTests
{
    [Fact]
    public void Provide_ThenConsume_DescendantSeesValue()
    {
        var sp = RenderHarness.EmptyServices();
        var consumer = new ThemeConsumer();
        var root = new StubComponent(() =>
            Context.Provide<Theme>(Value: new Theme("light"))[consumer]);

        var html = root.RenderAsLiveRoot(sp);

        Assert.Equal("light", consumer.LastSeen);
        Assert.Contains("light", html);
    }

    [Fact]
    public void NestedProviders_NearestWins()
    {
        var sp = RenderHarness.EmptyServices();
        var outer = new ThemeConsumer();
        var inner = new ThemeConsumer();
        var root = new StubComponent(() =>
            Context.Provide<Theme>(Value: new Theme("outer"))[
                outer,
                Context.Provide<Theme>(Value: new Theme("inner"))[inner]
            ]);

        root.RenderAsLiveRoot(sp);

        Assert.Equal("outer", outer.LastSeen);
        Assert.Equal("inner", inner.LastSeen);
    }

    [Fact]
    public void NoProvider_RequiredThrows_GetReturnsDefault_HasFalse()
    {
        var sp = RenderHarness.EmptyServices();
        var probe = new ContextProbe();
        var root = new StubComponent(() => new Fragment(probe));

        root.RenderAsLiveRoot(sp);

        Assert.False(probe.Has);
        Assert.Null(probe.GotOptional);
        Assert.True(probe.RequiredThrew);
    }

    [Fact]
    public void NullValue_ResolvesAsNull_DistinctFromMissing()
    {
        var sp = RenderHarness.EmptyServices();
        var probe = new ContextProbe();
        // A provider explicitly supplying a null reference: Has is true, the value is null, and
        // Required returns null rather than throwing — the "no provider" path.
        var root = new StubComponent(() =>
            Context.Provide<Theme?>(Value: null)[probe]);

        root.RenderAsLiveRoot(sp);

        Assert.True(probe.Has);
        Assert.Null(probe.GotOptional);
        Assert.False(probe.RequiredThrew);
    }

    [Fact]
    public void ValueType_Propagates()
    {
        var sp = RenderHarness.EmptyServices();
        var probe = new IntProbe();
        var root = new StubComponent(() => Context.Provide<int>(Value: 42)[probe]);

        root.RenderAsLiveRoot(sp);

        Assert.Equal(42, probe.Got);
    }

    [Fact]
    public void ProvidedAsConcrete_ConsumedByInterface()
    {
        var sp = RenderHarness.EmptyServices();
        var probe = new GreeterProbe();
        var root = new StubComponent(() =>
            Context.Provide<EnGreeter>(Value: new EnGreeter())[probe]);

        root.RenderAsLiveRoot(sp);

        // requested IGreeter is assignable from the provider's declared EnGreeter.
        Assert.Equal("hi", probe.Greeting);
    }

    [Fact]
    public void NamedProviders_ResolveIndependentlyBySameType()
    {
        var sp = RenderHarness.EmptyServices();
        var probe = new NamedProbe();
        var root = new StubComponent(() =>
            Context.Provide<string>(Value: "primary", Name: "a")[
                Context.Provide<string>(Value: "secondary", Name: "b")[probe]
            ]);

        root.RenderAsLiveRoot(sp);

        Assert.Equal("primary", probe.A);
        Assert.Equal("secondary", probe.B);
        Assert.Null(probe.Unnamed); // no nameless provider in scope
    }

    [Fact]
    public void Provider_DoesNotLeakToSibling_OutsideItsSubtree()
    {
        var sp = RenderHarness.EmptyServices();
        var inner = new ContextProbe();
        var sibling = new ContextProbe();
        var root = new StubComponent(() => new Fragment(
            Context.Provide<Theme>(Value: new Theme("scoped"))[inner],
            sibling));

        root.RenderAsLiveRoot(sp);

        Assert.True(inner.Has);          // inside the provider subtree
        Assert.False(sibling.Has);       // stack popped before the sibling renders
    }

    [Fact]
    public void KeyedProvider_ForwardsKeyToFirstChildElement()
    {
        var sp = RenderHarness.EmptyServices();
        var root = new StubComponent(() =>
            Context.Provide<int>(Value: 1, Key: "k1")[Div()["body"]]);

        var html = root.RenderAsLiveRoot(sp);

        Assert.Contains("data-rask-key=\"k1\"", html);
    }

    [Fact]
    public void ChangedValue_RerendersConsumer_ButCachesNonConsumerSibling()
    {
        var sp = RenderHarness.EmptyServices();
        var consumer = new ThemeConsumer();
        var plain = new PlainChild();
        var host = new MutableThemeHost(new Theme("light"), consumer, plain);

        var html1 = host.RenderAsLiveRoot(sp);
        Assert.Contains("light", html1);
        Assert.Equal(1, consumer.RenderCount);
        Assert.Equal(1, plain.RenderCount);

        host.Theme = new Theme("dark");
        var html2 = host.RenderAsLiveRoot(sp);

        // The consumer read a context value, so it bypasses the render cache and re-runs to
        // observe the new value. The plain sibling has stable props and no context read, so it
        // stays cached.
        Assert.Contains("dark", html2);
        Assert.DoesNotContain("light", html2);
        Assert.Equal(2, consumer.RenderCount);
        Assert.Equal(1, plain.RenderCount);
    }

    [Fact]
    public void Get_OutsideRender_ReturnsDefault_RequiredThrows()
    {
        // No LiveRenderContext is active: the consumer-mark must no-op rather than NRE.
        Assert.Null(Context.Get<Theme>());
        Assert.False(Context.Has<Theme>());
        Assert.Throws<InvalidOperationException>(() => Context.Required<Theme>());
    }

    // ---- helpers ----

    private sealed record Theme(string Name);

    private interface IGreeter
    {
        string Hello();
    }

    private sealed class EnGreeter : IGreeter
    {
        public string Hello() => "hi";
    }

    private sealed class ThemeConsumer : Component
    {
        public int RenderCount;
        public string? LastSeen;

        protected override RenderResult Render()
        {
            RenderCount++;
            var theme = Context.Required<Theme>();
            LastSeen = theme.Name;
            return Span()[theme.Name];
        }
    }

    private sealed class PlainChild : Component
    {
        public int RenderCount;

        protected override RenderResult Render()
        {
            RenderCount++;
            return Span()["plain"];
        }
    }

    private sealed class ContextProbe : Component
    {
        public bool Has;
        public Theme? GotOptional;
        public bool RequiredThrew;

        protected override RenderResult Render()
        {
            Has = Context.Has<Theme>();
            GotOptional = Context.Get<Theme>();
            try
            {
                _ = Context.Required<Theme>();
            }
            catch (InvalidOperationException)
            {
                RequiredThrew = true;
            }

            return new Fragment();
        }
    }

    private sealed class IntProbe : Component
    {
        public int Got;

        protected override RenderResult Render()
        {
            Got = Context.Required<int>();
            return new Fragment();
        }
    }

    private sealed class GreeterProbe : Component
    {
        public string? Greeting;

        protected override RenderResult Render()
        {
            Greeting = Context.Required<IGreeter>().Hello();
            return new Fragment();
        }
    }

    private sealed class NamedProbe : Component
    {
        public string? A;
        public string? B;
        public string? Unnamed;

        protected override RenderResult Render()
        {
            A = Context.Get<string>("a");
            B = Context.Get<string>("b");
            Unnamed = Context.Get<string>();
            return new Fragment();
        }
    }

    private sealed class MutableThemeHost : Component
    {
        private readonly Component _consumer;
        private readonly Component _plain;
        public Theme Theme;

        public MutableThemeHost(Theme initial, Component consumer, Component plain)
        {
            Theme = initial;
            _consumer = consumer;
            _plain = plain;
        }

        protected override RenderResult Render()
        {
            var ctx = LiveRenderContext.Current!;
            var con = ctx.GetOrCreate(_ => _consumer);
            ctx.NotifyParameters(con, false);
            var pl = ctx.GetOrCreate(_ => _plain);
            ctx.NotifyParameters(pl, false);
            return Context.Provide<Theme>(Value: Theme)[con, pl];
        }
    }
}
