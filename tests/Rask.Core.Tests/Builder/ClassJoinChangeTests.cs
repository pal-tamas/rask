using Rask.Core.Live;

#pragma warning disable RASK014 // the tests need the very instance they hand to the render context

namespace Rask.Core.Tests.Builder;

// A joined class reports a prop change only when the FINAL list differs from the one the last render ended
// with. The first `.Class("a")` of a pass always differs from last pass's "a b", so judging each write on its
// own would dirty the component on every render: its update hook would run and the render cache would miss.
internal sealed partial class JoinedLeaf : Component
{
    public string? Class { get; set; }

    internal int PropsChanges;
    internal int Renders;

    protected override Task OnUpdated()
    {
        PropsChanges++;
        return Task.CompletedTask;
    }

    protected override Component? Render()
    {
        Renders++;
        return Span.Class(Class)["x"];
    }
}

internal sealed partial class JoinedHost : Component
{
    internal string Second = "b";
    internal bool KeyInTheMiddle;
    internal JoinedLeaf? Leaf;

    protected override Component? Render() =>
        Div[Leaf = KeyInTheMiddle
            ? JoinedLeaf.Class("a").Key(1).Class(Second)
            : JoinedLeaf.Class("a").Class(Second)];
}

public class ClassJoinChangeTests
{
    private static string RenderLive(Component host, IServiceProvider sp)
    {
        using var ctx = LiveRenderContext.Begin(host, sp);
        var resolved = ctx.GetOrCreate(_ => host);
        LiveRenderContext.NotifyParameters(resolved, propsChanged: true);
        return resolved.ToHtml();
    }

    [Fact]
    public void A_joined_class_that_ends_where_it_did_last_render_reports_no_change()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new JoinedHost();

        for (var i = 0; i < 6; i++)
        {
            RenderLive(host, sp);
        }

        Assert.Equal(1, host.Leaf!.PropsChanges);
        Assert.Equal(1, host.Leaf.Renders);
    }

    [Fact]
    public void A_joined_class_whose_second_part_moves_reports_the_change_once()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new JoinedHost();
        RenderLive(host, sp);
        host.Second = "c";

        var html = RenderLive(host, sp);
        RenderLive(host, sp);

        Assert.Contains("<span class=\"a c\">", html, StringComparison.Ordinal);
        Assert.Equal(2, host.Leaf!.PropsChanges);
        Assert.Equal(2, host.Leaf.Renders);
    }

    [Fact]
    public void A_joined_class_split_around_Key_reports_no_change_when_it_ends_the_same()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new JoinedHost { KeyInTheMiddle = true };

        for (var i = 0; i < 6; i++)
        {
            RenderLive(host, sp);
        }

        Assert.Equal(1, host.Leaf!.PropsChanges);
        Assert.Equal(1, host.Leaf.Renders);
    }
}
