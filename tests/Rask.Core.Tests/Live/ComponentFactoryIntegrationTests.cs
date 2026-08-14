#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

public partial class ComponentFactoryIntegrationTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Factory_PassesRequiredProperty()
    {
        var instance = Generated.GreetCard("world");
        Assert.Equal("world", instance.Name);
        Assert.Null(instance.Subtitle);
        Assert.Equal("<span>world</span>", instance.ToHtml());
    }

    [Fact]
    public void Factory_OptionalNullableDefaultsToNull()
    {
        var instance = Generated.GreetCard("hello");
        Assert.Null(instance.Subtitle);
    }

    [Fact]
    public void Factory_AcceptsNamedOptionalArgument()
    {
        var instance = Generated.GreetCard("hello", "world");
        Assert.Equal("hello", instance.Name);
        Assert.Equal("world", instance.Subtitle);
        Assert.Equal("<span>hello: world</span>", instance.ToHtml());
    }

    [Fact]
    public void Factory_InsideContext_PreservesInstanceAcrossRenders_AndReappliesProps()
    {
        var services = RenderHarness.EmptyServices();

        var currentName = "alice";
        GreetCard? captured = null;
        var root = new StubComponent(() =>
        {
            var card = Generated.GreetCard(currentName);
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

    public sealed class GreetCard : Component
    {
        public required string Name { get; set; }
        public string? Subtitle { get; set; }

        protected override Component? Render() =>
            Span[Text.Value(Subtitle is null ? Name : $"{Name}: {Subtitle}")];
    }
}
