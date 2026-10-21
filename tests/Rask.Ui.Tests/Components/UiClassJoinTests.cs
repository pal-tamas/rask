using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;

#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it

namespace Rask.UiTests.Components;

// A second `.Class(…)` on a kit component adds to the first, and so does a second `.ContainerClass(…)`. The kit also
// writes its own classes onto inner elements during its Render, so re-rendering must not grow either list.
public partial class UiClassJoinTests : global::Rask.Core.RaskMarkup
{
    private sealed class Page : global::Rask.Core.Component
    {
        protected override global::Rask.Core.Component? Render() =>
            Div[
                Ui.Table.Class("w-full").Class("mb-8").ContainerClass("max-h-80").ContainerClass("rounded")[
                    Ui.TableRows[Ui.TableRow.Key(1)[Ui.TableCell.Class("a").Class("b")["x"]]]]];
    }

    private static string RenderLive(global::Rask.Core.Component page, IServiceProvider services)
    {
        using var ctx = LiveRenderContext.Begin(page, services);
        var resolved = ctx.GetOrCreate(_ => page);
        LiveRenderContext.NotifyParameters(resolved, propsChanged: true);
        return resolved.ToHtml();
    }

    [Fact]
    public void A_kit_table_keeps_both_classes_and_both_container_classes()
    {
        var table = Ui.Table.Class("w-full").ContainerClass("max-h-80");

        var html = table.Class("mb-8").ContainerClass("rounded").ToHtml();

        Assert.Contains(" w-full mb-8\"", html, StringComparison.Ordinal);
        Assert.Contains(" max-h-80 rounded\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Re_rendering_a_kit_table_five_times_leaves_its_classes_unchanged()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var page = new Page();
        var first = RenderLive(page, services);

        var renders = Enumerable.Range(0, 5).Select(_ => RenderLive(page, services)).ToList();

        Assert.Contains(" w-full mb-8\"", first, StringComparison.Ordinal);
        Assert.Contains(" max-h-80 rounded\"", first, StringComparison.Ordinal);
        Assert.Contains(" a b\"", first, StringComparison.Ordinal);
        Assert.All(renders, html => Assert.Equal(first, html));
    }
}
