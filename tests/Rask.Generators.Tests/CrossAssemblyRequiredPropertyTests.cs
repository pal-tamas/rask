using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

/// <summary>
///     What RASK038 can and cannot see once the component lives in a REFERENCED assembly — the shape
///     every Rask.Bootstrap component has from an app's point of view.
/// </summary>
public class CrossAssemblyRequiredPropertyTests
{
    private const string Library = """
        using Rask.Core;

        namespace Lib
        {
            public sealed class Card : Component
            {
                public string Title { get; set; }
                public string Kind { get; set; } = "plain";
                public required string Slug { get; set; }
                public string? Note { get; set; }
            }
        }
        """;

    private const string Consumer = """
        using Rask.Core;

        namespace Demo
        {
            public abstract class Entries : Component
            {
                protected static Lib.Card Card => null!;
            }

            public sealed class Page : Entries
            {
                protected override Component? Render() => Card;
            }
        }
        """;

    [Fact]
    public async Task A_required_modifier_survives_metadata_so_RASK038_still_fires()
    {
        var d = Assert.Single(await Diagnostics());
        Assert.Equal("RASK038", d.Id);
        Assert.Contains("'Slug'", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_RASK001_required_property_is_invisible_across_an_assembly_boundary()
    {
        // `Title` is non-nullable with no initializer — required by RASK001's rule, and reported when the
        // component is in the same compilation (RequiredBuilderPropertyAnalyzerTests). From metadata the
        // initializer is not observable, so `Title` and `Kind` look identical and neither is reported.
        var messages = (await Diagnostics()).Select(x => x.GetMessage()).ToList();
        Assert.DoesNotContain(messages, m => m.Contains("'Title'", StringComparison.Ordinal));
        Assert.DoesNotContain(messages, m => m.Contains("'Kind'", StringComparison.Ordinal));
    }

    /// <summary>
    ///     The other half of why <c>BlocksEntry</c> withholds an entry: an analyzer is not the only thing
    ///     in the way. <c>BuilderRuntime.Entry&lt;T&gt;</c> is constrained <c>where T : Component, new()</c>
    ///     and a type with a <c>required</c> member cannot satisfy <c>new()</c> at all, so those
    ///     components could not be handed an entry even if the call site were perfectly policed.
    /// </summary>
    [Fact]
    public void A_required_member_cannot_satisfy_the_new_constraint_Entry_needs()
    {
        var compilation = CSharpCompilation.Create(
            "NewConstraint",
            new[] { CSharpSyntaxTree.ParseText("""
                public class Card { public required string Title { get; set; } }
                public static class Runtime { public static T Entry<T>() where T : new() => new T(); }
                public static class Call { public static object Use() => Runtime.Entry<Card>(); }
                """, new CSharpParseOptions(LanguageVersion.Latest)) },
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        Assert.Contains(
            compilation.GetDiagnostics(),
            d => d.Severity == DiagnosticSeverity.Error && d.Id == "CS9040");
    }

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics()
    {
        var references = GeneratorDriverFixture.BuildReferences();
        var library = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(Library, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        using var stream = new MemoryStream();
        var emit = library.Emit(stream);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        stream.Position = 0;

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(Consumer, new CSharpParseOptions(LanguageVersion.Latest)) },
            references.Add(MetadataReference.CreateFromStream(stream)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        return await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new RequiredBuilderPropertyAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
    }
}
