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

    [Fact]
    public void A_read_face_parameter_is_named_by_its_full_name_and_the_lambda_call_is_intercepted()
    {
        var source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Authorization;
            using Rask.Core;
            using Rask.Data;
            using Ta.Data;

            namespace Ta.Data
            {
                public sealed class Company : Aggregate<Guid>
                {
                    private Company() { }
                    public string Name { get; private set; } = "";
                }
            }

            namespace Ta.Features.Companies
            {
                public sealed class CompaniesPage : Component
                {
                    protected override Component? Render() => null;

                    public Func<Task> Clicked(CompanyRead company) => () => Remove(company, null);

                    [Authorize(Roles = "admin")]
                    private async Task Remove(CompanyRead company, CompanyRead? other) { await Task.Yield(); }
                }
            }
            """;

        var (generated, output) = Run(
            source,
            [new Rask.Batteries.Generators.ReadModelGenerator()],
            "Rask.Data", "Microsoft.EntityFrameworkCore");

        Assert.Contains("global::Ta.Data.CompanyRead @company, global::Ta.Data.CompanyRead? @other", generated, StringComparison.Ordinal);
        Assert.Contains("typeof(global::Ta.Data.CompanyRead), typeof(global::Ta.Data.CompanyRead)", generated, StringComparison.Ordinal);
        Assert.Empty(Errors(output));
        Assert.Equal("Remove0", InterceptorOf(output, "Remove"));
    }

    [Fact]
    public void A_parameter_whose_type_another_generator_makes_in_the_pages_namespace_is_named_there()
    {
        var source = """
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Authorization;
            using Rask.Core;

            namespace Ta.Features.Companies;

            public sealed class CompaniesPage : Component
            {
                protected override Component? Render() => null;

                public System.Func<Task> Clicked(CompanyRow company) => () => Remove(company);

                [Authorize(Roles = "admin")]
                private async Task Remove(CompanyRow company) { await Task.Yield(); }
            }
            """;

        var (generated, output) = Run(source, [new ForeignTypeGenerator("Ta.Features.Companies", "record CompanyRow(int Id)")]);

        Assert.Contains("using global::Ta.Features.Companies;", generated, StringComparison.Ordinal);
        Assert.Empty(Errors(output));
        Assert.Equal("Remove0", InterceptorOf(output, "Remove"));
    }

    [Fact]
    public void A_parameter_whose_type_another_generator_makes_behind_the_files_using_is_named_through_it()
    {
        var source = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Authorization;
            using Rask.Core;
            using Ta.Rows;

            namespace Ta.Features.Companies
            {
                public sealed class CompaniesPage : Component
                {
                    protected override Component? Render() => null;

                    public System.Func<Task<CompanyRow?>> Clicked(List<CompanyRow> companies) => () => Remove(companies, null);

                    [Authorize(Roles = "admin")]
                    private async Task<CompanyRow?> Remove(List<CompanyRow> companies, CompanyRow[]? also)
                    {
                        await Task.Yield();
                        return companies[0];
                    }
                }
            }
            """;

        var (generated, output) = Run(source, [new ForeignTypeGenerator("Ta.Rows", "record CompanyRow(int Id)")]);

        Assert.Contains("using global::Ta.Rows;", generated, StringComparison.Ordinal);
        Assert.Empty(Errors(output));
        Assert.Equal("Remove0", InterceptorOf(output, "Remove"));
    }

    [Fact]
    public void A_nearer_namespace_wins_over_the_files_using_as_it_does_where_the_method_is_written()
    {
        var source = """
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Authorization;
            using Rask.Core;
            using Ta.Rows;

            namespace Ta.Features.Companies;

            public sealed class CompaniesPage : Component
            {
                protected override Component? Render() => null;

                public System.Func<Task> Clicked(CompanyRow company) => () => Remove(company);

                [Authorize(Roles = "admin")]
                private async Task Remove(CompanyRow company) { await Task.Yield(); }
            }
            """;

        var (_, output) = Run(
            source,
            [
                new ForeignTypeGenerator("Ta.Rows", "record CompanyRow(int Id)"),
                new ForeignTypeGenerator("Ta.Features", "record CompanyRow(string Name)"),
            ]);

        Assert.Empty(Errors(output));
        Assert.Equal("Remove0", InterceptorOf(output, "Remove"));
    }

    [Fact]
    public void A_nullable_parameter_of_a_type_no_generator_here_can_tell_from_a_struct_is_reported_and_left_alone()
    {
        var source = """
            using Microsoft.AspNetCore.Authorization;
            using Rask.Core;

            namespace Ta.Features.Companies;

            public sealed class CompaniesPage : Component
            {
                protected override Component? Render() => null;

                public System.Action Clicked() => () => Remove(null);

                [Authorize(Roles = "admin")]
                private void Remove(CompanyRow? company) { }
            }
            """;

        var run = Run(source, [new ForeignTypeGenerator("Ta.Features.Companies", "record CompanyRow(int Id)")]);

        Assert.DoesNotContain("\"Remove\"", run.Generated, StringComparison.Ordinal);
        Assert.Empty(Errors(run.Output));
        var reported = Assert.Single(run.Reported);
        Assert.Equal("RASK109", reported.Id);
        Assert.Contains("made by another generator", reported.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    // The generated file, and the compilation it joins with interceptors switched on as Rask.Core.targets does.
    // Other generators may run beside it, as Rask.Data's read faces do: none sees what another writes.
    private static GuardedRun Run(string source, IIncrementalGenerator[]? beside = null, params string[] assemblies)
    {
        var options = new CSharpParseOptions(LanguageVersion.Latest)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "Rask.Generated.Authorization")]);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source, options, "Page.cs")],
            [.. GeneratorDriverFixture.BuildReferences(), .. assemblies.Select(GeneratorDriverFixture.AssemblyReference)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        IIncrementalGenerator[] generators = [new HandlerAuthorizationGenerator(), .. beside ?? []];
        var driver = CSharpGeneratorDriver.Create(generators.Select(GeneratorExtensions.AsSourceGenerator), parseOptions: options)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _, TestContext.Current.CancellationToken);

        var generated = output.SyntaxTrees
            .FirstOrDefault(tree => tree.FilePath.EndsWith("__RaskGuardedHandlers.g.cs", StringComparison.Ordinal))
            ?.ToString() ?? string.Empty;
        return new GuardedRun(generated, output, driver.GetRunResult().Diagnostics);
    }

    private static IEnumerable<Diagnostic> Errors(Compilation output) =>
        output.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error);

    // The method the compiler sends the call `name(…)` in Page.cs to instead, or null when nothing intercepts it.
    private static string? InterceptorOf(Compilation output, string name)
    {
        var page = output.SyntaxTrees.First(tree => tree.FilePath == "Page.cs");
        var call = page.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
            .First(i => i.Expression.ToString() == name);
#pragma warning disable RSEXPERIMENTAL002 // the documented way to ask which interceptor a call binds to
        return output.GetSemanticModel(page).GetInterceptorMethod(call, TestContext.Current.CancellationToken)?.Name;
#pragma warning restore RSEXPERIMENTAL002
    }

    private sealed record GuardedRun(string Generated, Compilation Output, System.Collections.Immutable.ImmutableArray<Diagnostic> Reported)
    {
        public void Deconstruct(out string generated, out Compilation output)
        {
            generated = Generated;
            output = Output;
        }
    }

    // Writes a type the way another generator does — Rask.Data's read faces, a strongly-typed id — which the
    // generator under test cannot see.
    private sealed class ForeignTypeGenerator(string ns, string declaration) : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context) =>
            context.RegisterSourceOutput(context.CompilationProvider, (spc, _) => spc.AddSource(
                ns + ".g.cs", "namespace " + ns + " { public sealed " + declaration + "; }"));
    }
}

