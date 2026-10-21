using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rask.Generators.Authorization;

namespace Rask.Generators.Tests;

// A call to a component method with [Authorize] is intercepted where it is written, so a lambda that calls it is
// held to the method's rule; a call that cannot be intercepted says so (RASK109).
public class HandlerAuthorizationGeneratorTests
{
    private const string Page = """
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Authorization;
        using Rask.Core;

        namespace App;

        public sealed class TrailersPage : Component
        {
            private int _removed;

            protected override Component? Render() => null;

            public Task Clicked() => Remove(3);

            public void Tapped() => Archive();

            [Authorize(Roles = "admin")]
            private async Task Remove(int id) { await Task.Yield(); _removed += id; }

            [Authorize]
            private void Archive() => _removed = 0;

            private void Unguarded() => _removed = 1;

            public void Plain() => Unguarded();
        }
        """;

    [Fact]
    public void A_call_to_a_component_method_with_authorize_is_intercepted_and_the_result_compiles()
    {
        var source = Page;

        var (generated, output) = Run(source);

        Assert.Contains("await global::Rask.Core.Authorization.HandlerAccess.Demand(Method", generated, StringComparison.Ordinal);
        Assert.Contains("global::Rask.Core.Authorization.HandlerAccess.DemandNow(Method", generated, StringComparison.Ordinal);
        Assert.Contains("UnsafeAccessorKind.Method, Name = \"Remove\"", generated, StringComparison.Ordinal);
        Assert.Empty(output.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void A_method_without_authorize_is_not_intercepted()
    {
        var source = Page;

        var (generated, _) = Run(source);

        Assert.DoesNotContain("\"Unguarded\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_method_with_authorize_outside_a_component_is_left_to_aspnet()
    {
        var source = """
            using Microsoft.AspNetCore.Authorization;

            namespace App;

            public sealed class ReportsController
            {
                [Authorize(Roles = "admin")]
                public string Index() => "reports";

                public string Home() => Index();
            }
            """;

        var (generated, _) = Run(source);

        Assert.Equal(string.Empty, generated);
    }

    [Fact]
    public void A_call_to_a_generic_method_with_authorize_is_reported_as_unchecked()
    {
        var source = """
            using Microsoft.AspNetCore.Authorization;
            using Rask.Core;

            namespace App;

            public sealed class TrailersPage : Component
            {
                protected override Component? Render() => null;

                public void Clicked() => Remove<int>(3);

                [Authorize(Roles = "admin")]
                private void Remove<T>(T id) { }
            }
            """;

        var run = GeneratorDriverFixture.Run([("Page.cs", source)], [new HandlerAuthorizationGenerator()]);

        var reported = Assert.Single(run.RunResult.Diagnostics);
        Assert.Equal("RASK109", reported.Id);
        Assert.Contains("it is generic", reported.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    // The generated file, and the compilation it joins with interceptors switched on as Rask.Core.targets does.
    private static (string Generated, Compilation Output) Run(string source)
    {
        var options = new CSharpParseOptions(LanguageVersion.Latest)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "Rask.Generated.Authorization")]);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source, options, "Page.cs")],
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        CSharpGeneratorDriver.Create(new HandlerAuthorizationGenerator())
            .WithUpdatedParseOptions(options)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _, TestContext.Current.CancellationToken);

        var generated = output.SyntaxTrees.Skip(1).FirstOrDefault()?.ToString() ?? string.Empty;
        return (generated, output);
    }
}
