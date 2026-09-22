using Rask.Site;
using Rask.Site.Features;
using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Demos;

public sealed partial class PageHeaderTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void The_title_renders_as_a_heading_and_the_lead_as_a_paragraph()
    {
        var html = new LiveHost(
            () => PageHeader
                .Title("Greetings").Lead("A welcoming subtitle."),
            TestServices.Default()).RenderAsLiveRoot();

        // <h1> is the heading LEVEL; the utilities set its size, which used to be Bootstrap's .h2.
        Assert.Contains("<h1 class=\"text-3xl font-bold mb-2\">Greetings</h1>", html);
        Assert.Contains(
            "<p class=\"text-lg text-ui-muted mb-0\">A welcoming subtitle.</p>", html);
    }

    [Fact]
    public void The_header_HTML_encodes_its_content()
    {
        var html = new LiveHost(
            () => PageHeader.Title("<a>").Lead("&amp;"),
            TestServices.Default()).RenderAsLiveRoot();

        Assert.Contains("&lt;a&gt;", html);
    }
}
