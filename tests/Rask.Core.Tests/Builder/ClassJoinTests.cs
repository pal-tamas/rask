using Rask.Core.Live;

#pragma warning disable RASK014 // the tests need the very instance they hand to the render context

namespace Rask.Core.Tests.Builder;

// A second `.Class(…)` ADDS to the first, the way Blade's `$attributes->merge(['class' => …])` does — it used to
// replace it, and `Ui.Table.Class("w-full").Class("mb-8")` lost `w-full` with nothing to say so. A chain re-runs
// on every render against the instance the last render left, so the join starts again from nothing each pass.
internal sealed partial class ClassLeaf : Component
{
    public string? Class { get; set; }

    public string? LabelClass { get; set; }

    protected override Component? Render() =>
        Div.Class("leaf").Class(Class)[Span.Class(LabelClass)["x"]];
}

internal sealed partial class ClassJoinHost : Component
{
    internal int[] Keys = [1, 2];

    protected override Component? Render() =>
        Div[
            P.Class("w-full").Class("mb-8"),
            Keys.Select(k => ClassLeaf.Key(k).Class("a").Class("b").LabelClass("l1").LabelClass("l2"))];
}

internal sealed partial class ClassAfterKeyHost : Component
{
    protected override Component? Render() =>
        Div[ClassLeaf.Class("a").Key(1).Class("b")];
}

public partial class ClassJoinTests : RaskMarkup
{
    private static string RenderLive(Component host, IServiceProvider sp)
    {
        using var ctx = LiveRenderContext.Begin(host, sp);
        var resolved = ctx.GetOrCreate(_ => host);
        LiveRenderContext.NotifyParameters(resolved, propsChanged: true);
        return resolved.ToHtml();
    }

    [Fact]
    public void A_second_Class_adds_to_the_first()
    {
        var table = Table.Class("w-full");

        var html = table.Class("mb-8").ToHtml();

        Assert.Equal("<table class=\"w-full mb-8\"></table>", html);
    }

    [Fact]
    public void A_joined_Class_trims_its_parts_and_skips_blank_ones()
    {
        string? none = null;

        var html = Div.Class("p-4").Class(none).Class("  ").Class(" rounded ", "w-full").ToHtml();

        Assert.Equal("<div class=\"p-4 rounded w-full\"></div>", html);
    }

    [Fact]
    public void A_single_Class_is_written_as_given()
    {
        var value = "p-4";

        var div = Div.Class(value);

        Assert.Same(value, div.Class);
    }

    [Fact]
    public void Re_rendering_five_times_leaves_every_joined_class_unchanged()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new ClassJoinHost();
        var first = RenderLive(host, sp);

        var renders = Enumerable.Range(0, 5).Select(_ => RenderLive(host, sp)).ToList();

        Assert.Contains("<p class=\"w-full mb-8\"></p>", first, StringComparison.Ordinal);
        Assert.Equal(2, first.Split("<div class=\"leaf a b\" data-rask-key=").Length - 1);
        Assert.Equal(2, first.Split("<span class=\"l1 l2\">").Length - 1);
        Assert.All(renders, html => Assert.Equal(first, html));
    }

    [Fact]
    public void A_reused_keyed_child_starts_its_join_fresh_after_its_siblings_move()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new ClassJoinHost();
        _ = RenderLive(host, sp);
        host.Keys = [2, 1];

        var html = RenderLive(host, sp);

        Assert.Equal(2, html.Split("class=\"leaf a b\"").Length - 1);
        Assert.DoesNotContain("a b a", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Class_written_before_and_after_Key_joins_on_the_instance_the_key_keeps()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new ClassAfterKeyHost();

        var renders = Enumerable.Range(0, 5).Select(_ => RenderLive(host, sp)).ToList();

        Assert.All(renders, html => Assert.Contains("class=\"leaf a b\"", html, StringComparison.Ordinal));
    }
}
