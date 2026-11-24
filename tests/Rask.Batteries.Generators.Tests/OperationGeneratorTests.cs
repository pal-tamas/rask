using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>
///     F-42 + F-16: every public method of a partial aggregate gets a static entry of the same name that runs it as one
///     write, and the roles its Configure names are asked by that entry.
/// </summary>
public class OperationGeneratorTests
{
    private const string Supply = """
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using Rask.Data;

        namespace Shop
        {
            public static class Roles { public const string Admin = "Admin"; }

            public sealed partial class Supply : Aggregate<System.Guid, Versioned>
            {
                public string Note { get; private set; } = "";
                public int Opening { get; private set; }

                public void Change(string note, IReadOnlyList<int> parts) => Note = note + parts.Count;
                public Supply Rename(string note) { Note = note; return this; }
                public async Task Distribute(int count = 3) { await Task.Yield(); Opening = count; }
                public void SetOpening(int opening) => Opening = opening;
                public bool IsOpen() => Opening > 0;
                public static void Configure(EntityTypeBuilder<Supply> b) => b.Operation(s => s.SetOpening, roles: Roles.Admin);
            }
        }
        """;

    [Fact]
    public void Each_public_method_that_changes_the_aggregate_gets_an_entry_of_its_name_that_compiles()
    {
        var run = Run(Supply);

        var generated = run.GeneratedSource("Shop.Supply.Operations");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("public static global::System.Threading.Tasks.Task Change(global::System.Guid id, int version, string note, global::System.Collections.Generic.IReadOnlyList<int> parts, global::System.Threading.CancellationToken cancellationToken = default) =>", generated, StringComparison.Ordinal);
        Assert.Contains("Run<global::Shop.Supply>(\"Change\", null, id!, version, aggregate => { aggregate.Change(note, parts); return global::System.Threading.Tasks.Task.CompletedTask; }, cancellationToken);", generated, StringComparison.Ordinal);
        Assert.Contains("aggregate => { aggregate.Rename(note); return", generated, StringComparison.Ordinal);
        Assert.Contains("Distribute(global::System.Guid id, int version, int count = (int)3, global::System.Threading.CancellationToken", generated, StringComparison.Ordinal);
        Assert.Contains("aggregate => aggregate.Distribute(count), cancellationToken);", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_method_that_answers_a_question_and_the_conventions_get_no_entry()
    {
        var run = Run(Supply);

        var generated = run.GeneratedSource("Shop.Supply.Operations");

        Assert.DoesNotContain(" IsOpen(", generated, StringComparison.Ordinal);
        Assert.DoesNotContain(" Configure(", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void The_roles_its_Configure_names_are_asked_by_the_entry()
    {
        var run = Run(Supply);

        var generated = run.GeneratedSource("Shop.Supply.Operations");

        Assert.Contains("Run<global::Shop.Supply>(\"SetOpening\", \"Admin\", id!, version,", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void An_aggregate_with_no_version_is_found_by_its_id_alone()
    {
        var run = Run("""
            using Rask.Data;
            namespace Shop
            {
                public sealed partial class Tag : Aggregate<int>
                {
                    public string Name { get; private set; } = "";
                    public void Rename(string name) => Name = name;
                }
            }
            """);

        var generated = run.GeneratedSource("Shop.Tag.Operations");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("Rename(int id, string name, global::System.Threading.CancellationToken cancellationToken = default)", generated, StringComparison.Ordinal);
        Assert.Contains("(\"Rename\", null, id!, null, aggregate =>", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void An_aggregate_that_is_not_partial_gets_no_entries()
    {
        var run = Run("""
            using Rask.Data;
            namespace Shop
            {
                public sealed class Tag : Aggregate<int>
                {
                    public string Name { get; private set; } = "";
                    public void Rename(string name) => Name = name;
                }
            }
            """);

        var generated = run.RunResult.Results.SelectMany(static r => r.GeneratedSources);

        Assert.DoesNotContain(generated, static s => s.HintName.Contains(".Operations", StringComparison.Ordinal));
    }

    [Fact]
    public void An_operation_whose_parameter_takes_the_entrys_name_is_RASK171_and_gets_no_entry()
    {
        var run = Run("""
            using Rask.Data;
            namespace Shop
            {
                public sealed partial class Tag : Aggregate<int, Versioned>
                {
                    public int Level { get; private set; }
                    public void Bump(int version) => Level = version;
                }
            }
            """);

        var diagnostic = Assert.Single(run.RunResult.Diagnostics, static d => d.Id == "RASK171");

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("'Tag.Bump' has a parameter named 'version'", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain(run.RunResult.Results.SelectMany(static r => r.GeneratedSources), static s => s.HintName.Contains(".Operations", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Roles_that_are_not_a_constant_are_RASK172_and_no_entry_is_left_open()
    {
        var source = """
            private static readonly string Admins = "Admin";
            public int Level { get; private set; }
            public void Bump(int level) => Level = level;
            public static void Configure(EntityTypeBuilder<Tag> b) => b.Operation(t => t.Bump, roles: Admins);
            """;

        var run = Run(Tagged(source));
        var diagnostics = await Analyze(Tagged(source), new UnreadConfigurationAnalyzer());

        var diagnostic = Assert.Single(diagnostics, static d => d.Id == "RASK172");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("its roles are not a constant", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.False(run.HasGeneratedSource(".Operations"));
    }

    [Fact]
    public async Task Roles_named_outside_the_aggregates_Configure_are_RASK172()
    {
        var diagnostics = await Analyze(Tagged("""
            public int Level { get; private set; }
            public void Bump(int level) => Level = level;
            public static void Configure(EntityTypeBuilder<Tag> b) => Lock(b);
            private static void Lock(EntityTypeBuilder<Tag> b) => b.Operation(t => t.Bump, roles: "Admin");
            """), new UnreadConfigurationAnalyzer());

        var diagnostic = Assert.Single(diagnostics, static d => d.Id == "RASK172");
        Assert.Contains("it is made outside that method", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Constant_roles_in_the_aggregates_Configure_are_not_RASK172()
    {
        var diagnostics = await Analyze(Tagged("""
            public int Level { get; private set; }
            public void Bump(int level) => Level = level;
            public static void Configure(EntityTypeBuilder<Tag> b) => b.Operation(t => t.Bump, roles: "Admin");
            """), new UnreadConfigurationAnalyzer());

        Assert.DoesNotContain(diagnostics, static d => d.Id == "RASK172");
    }

    [Fact]
    public async Task A_bare_call_inside_the_aggregate_that_lands_on_the_entry_is_RASK170()
    {
        var diagnostics = await Analyze(Tagged("""
            public int Level { get; private set; }
            public void Bump(int level) => Level = level;
            public Task Again(Guid other) => Bump(other, 0, 4);
            """), new OperationEntryCallAnalyzer());

        var diagnostic = Assert.Single(diagnostics, static d => d.Id == "RASK170");

        Assert.Equal("Bump(other, 0, 4)", diagnostic.Flagged());
        Assert.Contains("'Bump(…)' inside 'Tag' calls the generated entry 'Tag.Bump(id, …)'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_entry_named_by_its_type_and_the_method_on_an_instance_are_not_RASK170()
    {
        var diagnostics = await Analyze(Tagged("""
            public int Level { get; private set; }
            public void Bump(int level) => Level = level;
            public Task Other(Guid other) => Tag.Bump(other, 0, 4);
            public void Twice(int level) { Bump(level); this.Bump(level); }
            """), new OperationEntryCallAnalyzer());

        Assert.DoesNotContain(diagnostics, static d => d.Id == "RASK170");
    }

    private static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(
            source,
            [new OperationGenerator()],
            "Rask.Data", "Rask.Wire", "Rask.Cqrs", "Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.Relational");

    // A partial, versioned Tag around the members given.
    private static string Tagged(string members) => $$"""
        using System;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using Rask.Data;
        namespace Shop
        {
            public sealed partial class Tag : Aggregate<Guid, Versioned>
            {
        {{members}}
            }
        }
        """;

    // The analyzer runs over the snippet AND the entries the generator writes for it, as a build does.
    private static async Task<IReadOnlyList<Diagnostic>> Analyze(string source, DiagnosticAnalyzer analyzer)
    {
        var run = Run(source);

        Assert.Empty(run.GeneratedCompileErrors());
        var generated = run.RunResult.Results
            .SelectMany(static r => r.GeneratedSources)
            .Select(static g => CSharpSyntaxTree.ParseText(g.SourceText, new CSharpParseOptions(LanguageVersion.Latest), path: g.HintName));
        return await run.Compilation.AddSyntaxTrees(generated)
            .WithAnalyzers(ImmutableArray.Create(analyzer))
            .GetAnalyzerDiagnosticsAsync();
    }
}
