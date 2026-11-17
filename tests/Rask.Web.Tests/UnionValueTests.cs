namespace Rask.Web.Tests;

/// <summary>
/// A C# union handed to a web API that takes any value goes out as its case value, and one read back as the app's own
/// type comes back as the case its properties name — with the host's options, which Rask.Web's own build on.
/// </summary>
public sealed class UnionValueTests
{
    public sealed record Search(string Term);

    public sealed record Pick(int Id);

    public union Selection(Search, Pick);

    [Fact]
    public async Task A_union_crosses_a_web_API_as_its_case_value_and_reads_back_as_the_case_the_browser_answered()
    {
        var browser = new HostedBrowser { Answer = """{"id":7}""" };

        Selection clone;
        using (FakeBrowser.Enter(browser))
        {
            clone = await Window.StructuredClone<Selection, Selection>(new Search("boots"));
        }

        Assert.Contains("""{"term":"boots"}""", browser.Calls[0].ArgsJson, StringComparison.Ordinal);
        Assert.Equal(new Pick(7), clone.Value);
    }
}
