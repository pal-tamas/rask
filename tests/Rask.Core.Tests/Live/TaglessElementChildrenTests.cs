using Entries = RaskEntriesRask_Core_Tests;

#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it
#pragma warning disable RASK022 // elements without a Key that move along are the subject of these tests

namespace Rask.Core.Tests.Live;

// An element that names no tag is drawn by its Render(), which wraps the children it was handed in markup of
// its own — the shape of most kit components. Its render used to be served from the cache as a tag's is,
// children and all: handed other children on a later render, it went on showing the earlier ones, and once
// its instance had moved to another place in its owner it showed ANOTHER element's.
public partial class TaglessElementChildrenTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_tagless_element_shows_the_children_it_is_handed_on_each_render()
    {
        var words = "first";
        var view = new StubComponent(() => Entries.WordsHost.Words(words));
        var first = view.RenderAsLiveRoot();

        words = "second";
        var second = view.RenderAsLiveRoot();

        Assert.Equal("<div><section><b>w</b>first</section></div>", first);
        Assert.Equal("<div><section><b>w</b>second</section></div>", second);
    }

    [Fact]
    public void A_tagless_element_that_moves_along_shows_its_own_children_and_not_its_neighbours()
    {
        var show = false;
        var view = new StubComponent(() => Entries.WordsHost.Words("a").Extra(show));
        var first = view.RenderAsLiveRoot();

        show = true;
        var shown = view.RenderAsLiveRoot();
        show = false;
        var hidden = view.RenderAsLiveRoot();

        Assert.Equal("<div><section><b>w</b>a</section></div>", first);
        Assert.Equal("<div><section><b>w</b>new</section><section><b>w</b>a</section></div>", shown);
        Assert.Equal(first, hidden);
    }

    [Fact]
    public void A_tagless_element_handed_no_children_is_still_served_from_its_cache()
    {
        WordsWrap.Renders = 0;
        var view = new StubComponent(() => Entries.WordsHost.Words(null));
        view.RenderAsLiveRoot();

        view.RenderAsLiveRoot();
        view.RenderAsLiveRoot();

        Assert.Equal(1, WordsWrap.Renders);
    }
}

/// <summary>Not the root, which renders on every frame: it re-renders only when its words change.</summary>
public sealed partial class WordsHost : Component
{
    public required string? Words { get; set; }

    public bool? Extra { get; set; }

    protected override Component? Render() =>
        Div[
            Extra is true ? Entries.WordsWrap["new"] : null,
            Words is null ? Entries.WordsWrap : Entries.WordsWrap[Words]
        ];
}

/// <summary>An element with no tag of its own, as a kit component is: its Render() wraps what it is handed.</summary>
public sealed partial class WordsWrap : Element
{
    internal static int Renders;

    protected override string? TagName => null;

    protected override Component? Render()
    {
        Renders++;
        return Section[B["w"], Children];
    }
}
