using Rask.Testing;

namespace Rask.Core.Tests.Components;

// Mount exists for components the app builds itself — a runtime-chosen type, a plugin, code compiled in
// the browser — which never pass through a generated factory's GetOrCreate and so are never adopted. The
// symptom of not adopting them is silent: they render, but no lifecycle hook ever fires, so anything that
// loads in OnMountAsync sits on its placeholder with nothing reported.
public sealed class MountTests
{
    private sealed class Loader : Component
    {
        public bool Mounted { get; private set; }
        public string State { get; private set; } = "loading…";

        protected override void OnMount() => Mounted = true;

        protected override async Task OnMountAsync()
        {
            await Task.Yield();
            State = "loaded";
        }

        protected override Component? Render() => Div(Class: "loader")[State];
    }

    [Fact]
    public void An_unadopted_instance_renders_but_never_mounts()
    {
        // The behaviour Mount exists to fix, pinned so the fix can't be mistaken for something that always
        // worked: handed straight over as a child, the instance renders and nothing else happens.
        var loader = NewLoader();
        var page = RaskTest.Render(() => Div(Class: "host")[loader]);

        Assert.Contains("loader", page.Html, StringComparison.Ordinal);
        Assert.False(loader.Mounted);
    }

    [Fact]
    public async Task Mount_runs_the_sync_and_async_lifecycle_of_an_instance_it_did_not_build()
    {
        var loader = NewLoader();
        var page = RaskTest.Render(() => Div()[Mount(Child: loader)]);

        Assert.True(loader.Mounted);

        // The async hook completing must also re-render through the handle adoption supplied — that is the
        // half that makes "load your data in OnMountAsync" work at all.
        await page.WaitForAsync(html => html.Contains("loaded", StringComparison.Ordinal));
    }

    [Fact]
    public void Mount_adds_no_markup_of_its_own()
    {
        var page = RaskTest.Render(() => Div(Class: "host")[Mount(Child: Span(Class: "child")["hi"])]);

        Assert.Contains("<div class=\"host\"><span class=\"child\">hi</span></div>", page.Html,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_null_child_renders_nothing()
    {
        var page = RaskTest.Render(() => Div(Class: "host")[Mount(Child: null)]);

        Assert.Contains("<div class=\"host\"></div>", page.Html, StringComparison.Ordinal);
    }

    // A component that DID come from a generated factory is already adopted; wrapping it must not
    // double-register it or re-run anything.
    [Fact]
    public void Wrapping_a_factory_built_child_is_harmless()
    {
        var page = RaskTest.Render(() => Div()[Mount(Child: Span(Class: "a")["x"])]);

        Assert.Contains("<span class=\"a\">x</span>", page.Html, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(page.Html, "<span"));
    }

    // Built the way a real caller reaching for Mount builds one: reflectively, because the type isn't known
    // until runtime. (It is also the only way to get an instance past RASK014, which is the analyzer's whole
    // point — a type you can name, you should build through its factory.)
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
