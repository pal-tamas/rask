#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

// A live update serializes the page straight into the session's buffer (#1141); it must be the same page
// the string path renders — head splice, encoded attributes and all.
public partial class LivePageIntoBufferTests : global::Rask.Core.RaskMarkup
{
    private static readonly IServiceProvider Services = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public void A_page_past_the_pooled_builder_cap_renders_into_the_buffer_as_it_does_to_a_string()
    {
        var expected = new BigPage(1_500).RenderAsLiveRoot(Services);
        using var buffers = new RenderedHtmlBuffers();

        new BigPage(1_500).RenderAsLiveRootInto(Services, publishOnly: false, buffers);

        Assert.True(expected.Length > 64 * 1024, $"the page is {expected.Length} chars, under the builder cap");
        Assert.Equal(expected, buffers.CurrentSpan.ToString());
    }

    [Fact]
    public void A_second_render_into_the_reused_buffer_matches_the_string_path_for_the_new_state()
    {
        var page = new BigPage(1_500);
        using var buffers = new RenderedHtmlBuffers();
        page.RenderAsLiveRootInto(Services, publishOnly: false, buffers);
        buffers.Commit();

        page.Rows = 20;
        page.RenderAsLiveRootInto(Services, publishOnly: false, buffers);

        Assert.Equal(new BigPage(20).RenderAsLiveRoot(Services), buffers.CurrentSpan.ToString());
    }

    private sealed class BigPage(int rows) : Component
    {
        public int Rows { get; set; } = rows;

        protected override Component? HeadAssets => Link.Rel("stylesheet").Href("/a.css?v=1&x=<2>");

        protected override Component? Render() =>
        [
            Doctype,
            Html.Lang("en")[
                Head,
                Body[
                    Ul.Class("rows")[Enumerable.Range(0, Rows).Select(i => Li.Key(i).Id($"r{i}").Title($"Row \"{i}\" & more")[$"Item {i} — é"])]
                ]
            ]
        ];
    }
}
