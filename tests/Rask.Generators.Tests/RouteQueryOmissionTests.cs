using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rask.Core.Routing;

namespace Rask.Generators.Tests;

// A generated URL says what its caller said: a query argument that was not given leaves its key out — for
// a non-nullable property too, so `[QueryParam] public int Page { get; set; } = 1;` links to `/orders` and
// the page shows its initializer — and one that was given is written, whatever its value.
public class RouteQueryOmissionTests
{
    private const string Orders =
        """
        using Rask.Core;
        using Rask.Core.Routing;
        namespace Demo;
        [Route("/orders")]
        public sealed partial class OrdersPage : Component
        {
            [QueryParam] public int Page { get; set; } = 1;
            [QueryParam("q")] public string Term { get; set; } = "";
            [QueryParam] public int? PerPage { get; set; }
            [QueryParam] public bool Open { get; set; }
            protected override Component? Render() => this;
        }
        """;

    private static readonly Lazy<Assembly> Built = new(() => Build(Orders));

    [Theory]
    [InlineData(null, null, null, null, "/orders")]
    [InlineData(3, null, null, null, "/orders?Page=3")]
    [InlineData(1, null, null, null, "/orders?Page=1")]
    [InlineData(0, null, null, null, "/orders?Page=0")]
    [InlineData(null, "tea", null, null, "/orders?q=tea")]
    [InlineData(null, null, 50, null, "/orders?PerPage=50")]
    [InlineData(null, null, null, false, "/orders?Open=false")]
    [InlineData(null, null, null, true, "/orders?Open=true")]
    [InlineData(3, "tea", null, null, "/orders?Page=3&q=tea")]
    [InlineData(3, null, 50, null, "/orders?Page=3&PerPage=50")]
    [InlineData(3, null, null, true, "/orders?Page=3&Open=true")]
    [InlineData(null, "tea", 50, null, "/orders?q=tea&PerPage=50")]
    [InlineData(null, "tea", null, true, "/orders?q=tea&Open=true")]
    [InlineData(null, null, 50, true, "/orders?PerPage=50&Open=true")]
    [InlineData(3, "tea", 50, null, "/orders?Page=3&q=tea&PerPage=50")]
    [InlineData(null, "tea", 50, true, "/orders?q=tea&PerPage=50&Open=true")]
    [InlineData(2, "a b&c", 50, true, "/orders?Page=2&q=a%20b%26c&PerPage=50&Open=true")]
    public void A_route_writes_the_query_keys_it_was_given_and_no_others(
        int? page, string? search, int? perPage, bool? open, string expected)
    {
        var routes = Built.Value.GetTypes().Single(t => t.Name == "Routes").GetMethod("OrdersPage")!;

        var url = (RouteUrl)routes.Invoke(null, [page, search, perPage, open])!;

        Assert.Equal(expected, url.Path + url.QueryString);
    }

    [Fact]
    public void An_empty_string_given_on_purpose_is_written_as_a_key_with_no_value()
    {
        var routes = Built.Value.GetTypes().Single(t => t.Name == "Routes").GetMethod("OrdersPage")!;

        var url = (RouteUrl)routes.Invoke(null, [null, string.Empty, null, null])!;

        Assert.Equal("/orders?q=", url.Path + url.QueryString);
    }

    [Theory]
    [InlineData(null, "/orders")]
    [InlineData(3, "/orders?Page=3")]
    [InlineData(1, "/orders?Page=1")]
    public void The_pages_own_Url_leaves_out_what_the_route_leaves_out(int? page, string expected)
    {
        var helper = Built.Value.GetTypes().Single(t => t.Name == "__RaskNav_OrdersPage").GetMethod("Url")!;

        var url = (RouteUrl)helper.Invoke(null, [page, null, null, null])!;

        Assert.Equal(expected, url.Path + url.QueryString);
    }

    [Fact]
    public void The_signatures_stay_what_they_were()
    {
        const string parameters = "(int? Page = null, string? Term = null, int? PerPage = null, bool? Open = null)";

        var output = GeneratorDriverFixture.RunRoutes(Orders).GeneratedSource("Routes.g.cs");

        Assert.Contains("public static global::Rask.Core.Routing.RouteUrl OrdersPage" + parameters, output, StringComparison.Ordinal);
        Assert.Contains("public static global::Rask.Core.Routing.RouteUrl Url" + parameters, output, StringComparison.Ordinal);
        Assert.Contains("public static global::Rask.Core.GoTo Go" + parameters, output, StringComparison.Ordinal);
    }

    // The generated source compiled into an assembly and loaded: what a link IS can only be read by calling it.
    private static Assembly Build(string source)
    {
        var run = GeneratorDriverFixture.RunRoutes(source);
        var generated = run.RunResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => CSharpSyntaxTree.ParseText(
                s.SourceText, new CSharpParseOptions(LanguageVersion.Latest), path: s.HintName));
        using var image = new MemoryStream();

        var emitted = run.Compilation.AddSyntaxTrees(generated).Emit(image);

        Assert.DoesNotContain(emitted.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        return Assembly.Load(image.ToArray());
    }
}
