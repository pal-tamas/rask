using Microsoft.CodeAnalysis;

namespace Rask.Generators.Tests;

// A non-nullable reference prop declared `= null!` (a page's `[RouteParam] public string Id { get; set; } = null!;`)
// has a constant initializer whose value is null, so it is an optional chain step defaulting to that value. The
// deferred pending reset writes that default back, and it has to carry the `!` the declaration carried, or an app
// built warnings-as-errors fails on CS8625 in a file it never wrote.
public class NullForgivingInitializerTests
{
    private const string Page = """
                                using Rask.Core;
                                namespace Demo;
                                [Route("/loaders/{id}")]
                                public partial class LoaderPage : Component
                                {
                                    [RouteParam] public string Id { get; set; } = null!;
                                    public string Heading { get; set; } = null!;
                                    public object Model { get; set; } = default!;
                                    public string? Note { get; set; } = null;
                                }
                                """;

    [Fact]
    public void A_null_forgiven_initializer_compiles_without_a_nullable_warning_in_generated_code()
    {
        var compilation = BuilderGeneratorHarness.Compile(Page);

        var warnings = compilation.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .Where(d => d.Location.SourceTree?.FilePath.EndsWith(".g.cs", StringComparison.Ordinal) == true)
            .Select(d => d.ToString())
            .ToList();

        Assert.True(warnings.Count == 0, string.Join(Environment.NewLine, warnings));
    }

    [Fact]
    public void A_null_forgiven_initializer_resets_to_null_forgiven_and_a_nullable_one_to_plain_null()
    {
        var run = BuilderGeneratorHarness.Run(Page);

        var setters = run.Source("RaskBuilderSetters.g.cs");

        Assert.Contains("__c.Id = null!;", setters, StringComparison.Ordinal);
        Assert.Contains("__c.Heading = null!;", setters, StringComparison.Ordinal);
        Assert.Contains("__c.Model = null!;", setters, StringComparison.Ordinal);
        Assert.Contains("__c.Note = null;", setters, StringComparison.Ordinal);
    }
}
