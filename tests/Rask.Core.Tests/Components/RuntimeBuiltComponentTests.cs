using Rask.Testing;

namespace Rask.Core.Tests.Components;

// A component the app builds itself — a runtime-chosen type, a plugin, code compiled in the browser — never
// passes through a chain entry, which is what registers a child and starts its lifecycle. It used to render
// with no lifecycle at all, silently, unless wrapped in a `Mount` component nobody knew to reach for. The walk
// adopts it now: put the instance in the tree and it behaves like any other child.
public sealed partial class RuntimeBuiltComponentTests : global::Rask.Core.RaskMarkup
{
    private sealed class Loader : Component
    {
        public int Mounts { get; private set; }
        public int Unmounts { get; private set; }
        public string State { get; private set; } = "loading…";

        protected override async Task OnMount()
        {
            Mounts++;
            await Task.Yield();
            State = "loaded";
        }

        protected override async Task OnUnmount() => Unmounts++;

        protected override Component? Render() => Div.Class("loader")[State];
    }

    [Fact]
    public async Task An_instance_placed_in_the_tree_mounts_and_repaints_when_its_hook_completes()
    {
        var loader = NewLoader();
        var page = RaskTest.Render(() => Div.Class("host")[loader]);

        Assert.Equal(1, loader.Mounts);

        // The half that makes "load your data in Mount" work at all: the async hook completing re-renders
        // through the handle adoption supplied.
        await page.WaitForAsync(html => html.Contains("loaded", StringComparison.Ordinal));
    }

    [Fact]
    public async Task It_stays_mounted_when_its_parent_renders_again_before_its_Mount_completes()
    {
        // The order that used to lose it: a parent rebuilds its child map every render, and the walk registered
        // a runtime-built instance only the first time — so the second render read it as removed, unmounted it
        // while it was on screen, and the repaint its Mount asked for never came. Gated here so the second
        // render always lands first, instead of only under load.
        var gated = NewGated();
        var page = RaskTest.Render(() => Div.Class("host")[gated]);

        page.Render();
        gated.Release();

        await page.WaitForAsync(html => html.Contains("loaded", StringComparison.Ordinal));
        Assert.Equal(0, gated.Unmounts);
    }

    private sealed class Gated : Component
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Unmounts { get; private set; }
        public string State { get; private set; } = "loading…";

        public void Release() => _gate.TrySetResult();

        protected override async Task OnMount()
        {
            await _gate.Task;
            State = "loaded";
        }

        protected override async Task OnUnmount() => Unmounts++;

        protected override Component? Render() => Div.Class("gated")[State];
    }

    private static Gated NewGated() => (Gated)Activator.CreateInstance(typeof(Gated))!;

    [Fact]
    public void It_mounts_once_however_many_times_its_parent_renders()
    {
        var loader = NewLoader();
        var page = RaskTest.Render(() => Div[loader]);

        page.Render();
        page.Render();

        Assert.Equal(1, loader.Mounts);
    }

    [Fact]
    public void It_unmounts_when_its_parent_stops_rendering_it()
    {
        var loader = NewLoader();
        var shown = true;
        var page = RaskTest.Render(() => Div[shown ? loader : null]);

        shown = false;
        page.Render();

        Assert.Equal(1, loader.Unmounts);
    }

    [Fact]
    public void Several_instances_of_one_type_under_one_parent_each_get_a_lifecycle()
    {
        // Adoption used to file every adopted child under one slot per type, which a single wrapper never
        // noticed and a row of plugin widgets would: all but the last would lose their lifecycle.
        var loaders = new[] { NewLoader(), NewLoader(), NewLoader() };
        RaskTest.Render(() => Div[loaders]);

        Assert.All(loaders, l => Assert.Equal(1, l.Mounts));
    }

    [Fact]
    public void A_chain_built_child_is_not_adopted_a_second_time()
    {
        var page = RaskTest.Render(() => Div[Span.Class("a")["x"]]);

        Assert.Contains("<span class=\"a\">x</span>", page.Html, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(page.Html, "<span"));
    }

    // Built the way a real caller builds one: reflectively, because the type isn't known until runtime. (It is
    // also the only way to get an instance past RASK014 — a type you can name, you build through its chain.)
    private static Loader NewLoader() => (Loader)Activator.CreateInstance(typeof(Loader))!;

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
