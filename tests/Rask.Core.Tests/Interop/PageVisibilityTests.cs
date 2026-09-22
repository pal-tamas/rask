using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class PageVisibilityTests
{
    [Theory]
    [InlineData("visible", PageVisibility.Visible)]
    [InlineData("hidden", PageVisibility.Hidden)]
    [InlineData("prerender", PageVisibility.Prerender)]
    [InlineData(null, PageVisibility.Visible)]
    public async Task Getting_the_state_reads_the_visibility_state_and_maps_the_enum(string? raw, PageVisibility expected)
    {
        var js = new FakeJsRuntime();
        if (raw is not null)
        {
            js.SetResponse("document.visibilityState", raw);
        }

        var visibility = new PageVisibilityInfo(js);

        Assert.Equal(expected, await visibility.GetStateAsync());
        Assert.Empty(js.ArgsFor("document.visibilityState")!);
    }

    [Fact]
    public async Task Asking_whether_hidden_reads_document_hidden()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("document.hidden", true);
        var visibility = new PageVisibilityInfo(js);

        Assert.True(await visibility.IsHiddenAsync());
    }
}
