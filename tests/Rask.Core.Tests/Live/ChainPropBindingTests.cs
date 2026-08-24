#pragma warning disable RASK014 // test-defined Component subclasses are built through the chain, not `new`

namespace Rask.Core.Tests.Live;

// What a chain does when it builds a component: take the required property as its opening step, leave an
// optional one null until a step names it, and — inside a live context — hand back the SAME instance on
// every render with the properties re-applied. That last one is the load-bearing part: it is what makes a
// component's own state survive a re-render of its parent.
public partial class ChainPropBindingTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void RequiredStep_SetsTheProperty_AndLeavesTheOptionalOneNull()
    {
        GreetCard instance = GreetCard.Name("world");

        Assert.Equal("world", instance.Name);
        Assert.Null(instance.Subtitle);
        Assert.Equal("<span>world</span>", instance.ToHtml());
    }

    [Fact]
    public void OptionalStep_SetsTheProperty()
    {
        GreetCard instance = GreetCard.Name("hello").Subtitle("world");

        Assert.Equal("hello", instance.Name);
        Assert.Equal("world", instance.Subtitle);
        Assert.Equal("<span>hello: world</span>", instance.ToHtml());
    }

    [Fact]
    public void InsideAContext_TheChainPreservesTheInstanceAcrossRenders_AndReappliesTheProps()
    {
        var services = RenderHarness.EmptyServices();

        var currentName = "alice";
        GreetCard? captured = null;
        var root = new StubComponent(() =>
        {
            GreetCard card = GreetCard.Name(currentName);
            captured = card;
            return card;
        });

        var html1 = root.RenderAsLiveRoot(services);
        var first = captured!;
        Assert.Equal("alice", first.Name);
        Assert.Contains("alice", html1);

        currentName = "bob";
        var html2 = root.RenderAsLiveRoot(services);
        var second = captured!;

        Assert.Same(first, second);
        Assert.Equal("bob", second.Name);
        Assert.Contains("bob", html2);
    }
}

// Top-level rather than nested in the test class: the generator injects an entry named after the component
// into every markup host, and a nested type of the same name would collide with it (CS0102).
public sealed partial class GreetCard : Component
{
    public required string Name { get; set; }
    public string? Subtitle { get; set; }

    protected override Component? Render() =>
        Span[Text.Value(Subtitle is null ? Name : $"{Name}: {Subtitle}")];
}
