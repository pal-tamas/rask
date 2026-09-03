using Rask.Example.Shared;
using Rask.Example.Shared.Features;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.Demos;

public sealed partial class PageHeaderTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_EmitsTitle_AsH2_AndLead_AsP()
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
    public void Render_HtmlEncodesContent()
    {
        var html = new LiveHost(
            () => PageHeader.Title("<a>").Lead("&amp;"),
            TestServices.Default()).RenderAsLiveRoot();
        Assert.Contains("&lt;a&gt;", html);
    }
}
