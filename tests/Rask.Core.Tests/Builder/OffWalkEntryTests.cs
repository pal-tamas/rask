#pragma warning disable RASK014 // the test needs the very instance it renders as the root

namespace Rask.Core.Tests.Builder;

// A hook's continuation carries the walk's render context after its await (an AsyncLocal), but is no longer inside
// that walk — and in a live session it runs on a pool thread beside the session's next one. A chain built there
// once filed itself into the tree through the captured context, writing a parent's child map while the next walk
// enumerated it: "Collection was modified" from Component.CollectAliveWithParents (a searching Ui.Pillbox, whose
// answer builds its option rows after the search's await).
internal sealed partial class LateBuildingPage : Component
{
    internal TaskCompletionSource<Component> Late { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task OnMount()
    {
        await Task.Yield();
        Late.TrySetResult(Span["late"]);
    }

    protected override Component? Render() => Div[Span["page"]];
}

public class OffWalkEntryTests
{
    [Fact]
    public async Task A_chain_built_after_a_hooks_await_is_filed_into_no_tree()
    {
        var page = new LateBuildingPage();
        page.RenderAsLiveRoot(RenderHarness.EmptyServices());

        var late = await page.Late.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(late, Descendants(page));
    }

    private static IEnumerable<Component> Descendants(Component root)
    {
        foreach (var child in root.PersistedChildren.Values)
        {
            yield return child;
            foreach (var below in Descendants(child))
            {
                yield return below;
            }
        }
    }
}
