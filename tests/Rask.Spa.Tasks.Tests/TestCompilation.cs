using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Cqrs.Generators;

namespace Rask.Spa.Tasks.Tests;

/// <summary>
///     Compiles C# contracts through the real CQRS generator and writes the assembly to disk.
/// </summary>
/// <remarks>
///     The real generator on purpose. The task finds its constants by looking up a namespace, a type
///     name and two field names as strings, and nothing in either compiler relates the two sides — so
///     a hand-written fixture would keep passing after a rename that had already broken the pipeline.
/// </remarks>
internal static class TestCompilation
{
    /// <summary>Compiles <paramref name="source" /> and returns the path of the emitted assembly.</summary>
    public static string Emit(string source, string directory, bool emitTypeScript = true)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest), path: "Source.cs");
        var compilation = CSharpCompilation.Create(
            "Probe",
            [tree],
            References(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new CqrsCodecGenerator())
            .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.Latest))
            .WithUpdatedAnalyzerConfigOptions(new Options(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_property.RaskEmitTypeScript"] = emitTypeScript ? "true" : "false",
            }));

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Probe.dll");
        var result = updated.Emit(path);
        Assert.True(
            result.Success,
            "The generated code did not compile: "
            + string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        return path;
    }

    private static IEnumerable<MetadataReference> References()
    {
        var trusted = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var path in trusted)
        {
            yield return MetadataReference.CreateFromFile(path);
        }

        // The transport assembly, which is what makes the generator run at all: it carries the
        // assembly-level attribute the generator looks for before emitting anything.
        yield return MetadataReference.CreateFromFile(Assembly.Load("Rask.Cqrs.Client").Location);
    }

    private sealed class Options(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Fixed(values);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

        private sealed class Fixed(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) =>
                values.TryGetValue(key, out value);
        }
    }
}
