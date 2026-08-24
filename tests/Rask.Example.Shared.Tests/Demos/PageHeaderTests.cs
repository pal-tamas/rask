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
        // PageHeader uses H1 with bootstrap class "h2" (visual sizing, not HTML tag).
        Assert.Contains("<h1 class=\"h2 fw-bold mb-2\">Greetings</h1>", html);
        Assert.Contains("<p class=\"lead text-secondary mb-0\">A welcoming subtitle.</p>", html);
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
