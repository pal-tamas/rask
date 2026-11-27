using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Rask.Batteries.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

/// <summary>RASK192's Fix all: every restated name and default of a document goes in one pass, two in one chain included.</summary>
public class ConventionRestatedFixAllTests
{
    private const string Source = """
        using System;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using Rask.Data;
        namespace Fuel;
        public sealed class Fueling : Aggregate<Guid>
        {
            private Fueling() { }
            public Guid PistolId { get; private set; }
            public DateTime CreatedAt { get; private set; }
            public static void Configure(EntityTypeBuilder<Fueling> b)
            {
                b.ToTable("fuelings");
                b.HasKey(x => x.Id).HasName("pk_fuelings");
                b.Property(x => x.PistolId).HasColumnName("pistol_id");
                b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
                b.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_fuelings_created_at");
                b.Property(x => x.Id).ValueGeneratedNever();
            }
        }
        """;

    [Fact]
    public async Task Fix_all_deletes_every_restated_name_and_default_in_the_document()
    {
        var document = Document(Source);

        var fixed_ = await FixAllAsync(document);

        Assert.DoesNotContain("ToTable", fixed_, StringComparison.Ordinal);
        Assert.DoesNotContain("HasName", fixed_, StringComparison.Ordinal);
        Assert.DoesNotContain("HasColumnName", fixed_, StringComparison.Ordinal);
        Assert.DoesNotContain("HasDatabaseName", fixed_, StringComparison.Ordinal);
        Assert.DoesNotContain("b.Property(x => x.PistolId)", fixed_, StringComparison.Ordinal);
        Assert.Contains("b.HasKey(x => x.Id);", fixed_, StringComparison.Ordinal);
        Assert.DoesNotContain("IsRequired", fixed_, StringComparison.Ordinal);
        Assert.DoesNotContain("ValueGeneratedNever", fixed_, StringComparison.Ordinal);
        Assert.Contains("b.HasIndex(x => x.CreatedAt);", fixed_, StringComparison.Ordinal);
    }

    private static async Task<string> FixAllAsync(Document document)
    {
        var analyzer = new ConventionRestatedAnalyzer();
        var provider = new ConventionRestatedCodeFixProvider();
        var compilation = (await document.Project.GetCompilationAsync())!;
        var diagnostics = await compilation.WithAnalyzers([analyzer]).GetAnalyzerDiagnosticsAsync();
        var context = new FixAllContext(
            document, provider, FixAllScope.Document, "RASK192", ["RASK192"], new Diagnostics(diagnostics), CancellationToken.None);

        var action = await provider.GetFixAllProvider().GetFixAsync(context);
        var operations = await action!.GetOperationsAsync(CancellationToken.None);
        var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
        return (await changed.GetTextAsync()).ToString();
    }

    private static Document Document(string source)
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(), VersionStamp.Create(), "Test", "Test", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            metadataReferences: GeneratorDriverFixture.BuildReferences().AddRange(
                new[] { "Rask.Data", "Rask.Cqrs", "Rask.Wire", "Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.Relational" }
                    .Select(GeneratorDriverFixture.AssemblyReference))));
        return workspace.AddDocument(project.Id, "Test.cs", SourceText.From(source));
    }

    private sealed class Diagnostics(ImmutableArray<Diagnostic> all) : FixAllContext.DiagnosticProvider
    {
        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>(all);

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>([]);

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>(all);
    }
}
