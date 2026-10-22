using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rask.Core.Routing;

namespace Rask.Generators.Tests;

// A [QueryParam] list is ONE key in a generated URL: `Routes.FleetPage(Drivers: ["ada", "theo"])` links to
// `/fleet?Drivers=ada%2Ctheo`, in the order given, and an empty list leaves the key out as an ungiven one does.
public class RouteQueryListTests
{
    private const string Fleet =
        """
        using System;
        using System.Collections.Generic;
        using Rask.Core;
        using Rask.Core.Routing;
        namespace Demo;
        [Route("/fleet")]
        public sealed partial class FleetPage : Component
        {
            [QueryParam] public IReadOnlyList<string> Drivers { get; set; } = [];
            [QueryParam("tank")] public int[] Tanks { get; set; } = [];
            protected override Component? Render() => this;
        }
        """;

    private static readonly Lazy<Assembly> Built = new(() => Build(Fleet));

    [Fact]
    public void A_list_is_written_as_one_key_with_its_elements_in_the_order_given()
    {
        string[] drivers = ["theo", "ada"];

        var url = Route(drivers, [3, 1]);

        Assert.Equal("/fleet?Drivers=theo%2Cada&tank=3%2C1", url);
    }

    [Fact]
    public void An_element_with_a_comma_is_written_as_it_is_and_splits_when_read()
    {
        string[] drivers = ["Lovelace, Ada", "theo"];

        var url = Route(drivers, null);

        Assert.Equal("/fleet?Drivers=Lovelace%2C%20Ada%2Ctheo", url);
    }

    [Fact]
    public void A_list_of_nothing_to_write_writes_no_key()
    {
        string[] drivers = ["", ""];

        var url = Route(drivers, [4]);

        Assert.Equal("/fleet?tank=4", url);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_empty_or_ungiven_list_writes_no_key(bool empty)
    {
        var drivers = empty ? Array.Empty<string>() : null;

        var url = Route(drivers, empty ? [] : null);

        Assert.Equal("/fleet", url);
    }

    [Fact]
    public void The_pages_own_Url_writes_a_list_as_the_route_does()
    {
        var helper = Built.Value.GetTypes().Single(t => t.Name == "__RaskNav_FleetPage").GetMethod("Url")!;

        var url = (RouteUrl)helper.Invoke(null, [new[] { "ada" }, new[] { 2 }])!;

        Assert.Equal("/fleet?Drivers=ada&tank=2", url.Path + url.QueryString);
    }

    [Fact]
    public void A_list_parameter_is_the_list_type_made_nullable()
    {
        const string parameters =
            "(global::System.Collections.Generic.IReadOnlyList<string>? Drivers = null, int[]? Tanks = null)";

        var output = GeneratorDriverFixture.RunRoutes(Fleet).GeneratedSource("Routes.g.cs");

        Assert.Contains("public static global::Rask.Core.Routing.RouteUrl FleetPage" + parameters, output,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("IReadOnlyList<string>")]
    [InlineData("IReadOnlyCollection<int>")]
    [InlineData("IEnumerable<Guid>")]
    [InlineData("DateOnly[]")]
    [InlineData("IReadOnlyList<bool>?")]
    public void A_query_list_of_a_readable_element_is_bindable(string type)
    {
        var source = Page($"[QueryParam] public {type} Value {{ get; set; }} = [];");

        var run = GeneratorDriverFixture.RunRoutes(source);

        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK011");
    }

    [Theory]
    [InlineData("[QueryParam] public List<int> Value { get; set; } = [];")]
    [InlineData("[QueryParam] public IReadOnlyList<object> Value { get; set; } = [];")]
    [InlineData("[QueryParam] public IReadOnlyList<int?> Value { get; set; } = [];")]
    [InlineData("[QueryParam] public int[][] Value { get; set; } = [];")]
    public void A_query_list_that_cannot_be_read_still_raises_RASK011(string property)
    {
        var source = Page(property);

        var run = GeneratorDriverFixture.RunRoutes(source);

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK011");
    }

    [Fact]
    public void A_list_registers_its_element_type_for_a_trimmed_build()
    {
        var source = Page("[QueryParam] public IReadOnlyList<Plate> Value { get; set; } = [];") + Plate;

        var run = GeneratorDriverFixture.RunRoutes(source);

        var generated = string.Concat(run.RunResult.Results.SelectMany(r => r.GeneratedSources)
            .Select(s => s.SourceText.ToString()));
        Assert.Contains("RegisterParsable<global::Demo.Plate>();", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_list_is_not_a_path_segment()
    {
        var source = Page("[RouteParam] public IReadOnlyList<int> Id { get; set; } = [];")
            .Replace("\"/x\"", "\"/x/{id}\"", StringComparison.Ordinal);

        var run = GeneratorDriverFixture.RunRoutes(source);

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK011");
    }

    private const string Plate =
        """

        public readonly record struct Plate(string Value) : IParsable<Plate>
        {
            public static Plate Parse(string s, IFormatProvider? provider) => new(s);
            public static bool TryParse(string? s, IFormatProvider? provider, out Plate result)
            {
                result = new(s ?? string.Empty);
                return s is not null;
            }
        }
        """;

    private static string Page(string property) =>
        $$"""
          using System;
          using System.Collections.Generic;
          using Rask.Core;
          using Rask.Core.Routing;
          namespace Demo;
          [Route("/x")]
          public sealed class XPage : Component
          {
              {{property}}
              protected override Component? Render() => this;
          }
          """;

    private static string Route(string[]? drivers, int[]? tanks)
    {
        var routes = Built.Value.GetTypes().Single(t => t.Name == "Routes").GetMethod("FleetPage")!;
        var url = (RouteUrl)routes.Invoke(null, [drivers, tanks])!;
        return url.Path + url.QueryString;
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
