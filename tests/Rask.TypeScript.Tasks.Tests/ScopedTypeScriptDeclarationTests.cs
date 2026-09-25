using System.Xml.Linq;

namespace Rask.TypeScript.Tasks.Tests;

/// <summary>
///     Pins the declaration half of the scoped-TS compile in <c>Rask.Core.targets</c>: tsgo writes a
///     <c>.d.ts</c> beside every <c>.js</c>, and it reaches the generator that turns a component's exports
///     into typed private methods (ScopedScriptCallsGenerator).
/// </summary>
public sealed class ScopedTypeScriptDeclarationTests
{
    private static readonly string _targets =
        Path.Combine(PinnedTools.RepositoryRoot(), "src", "Rask.Core", "build", "Rask.Core.targets");

    [Fact]
    public void The_scoped_compile_emits_declarations()
    {
        var elements = XDocument.Load(_targets).Descendants().ToList();

        var command = elements
            .Where(e => e.Name.LocalName == "Exec")
            .Select(e => (string?)e.Attribute("Command") ?? string.Empty)
            .Single(c => c.Contains("_RaskScopedTsOutDir", StringComparison.Ordinal));
        var compile = elements.Single(e =>
            e.Name.LocalName == "Target" && (string?)e.Attribute("Name") == "_RaskCompileScopedTs");

        Assert.Contains(" --declaration", command, StringComparison.Ordinal);
        Assert.Contains("%(Filename).d.ts", (string?)compile.Attribute("Outputs"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_declarations_reach_the_generator_tagged_with_their_source()
    {
        var elements = XDocument.Load(_targets).Descendants().ToList();

        var declarations = elements.Single(e => e.Name.LocalName == "_RaskCompiledScopedDts");
        var additional = elements.Where(e => e.Name.LocalName == "AdditionalFiles")
            .Select(e => (string?)e.Attribute("Include"));

        Assert.StartsWith("@(_RaskScopedTsTagged->", (string?)declarations.Attribute("Include"), StringComparison.Ordinal);
        Assert.Contains("@(_RaskCompiledScopedDts)", additional);
    }

    [Fact]
    public void The_tsgo_emit_writes_a_declaration_the_generator_can_read()
    {
        var directory = Directory.CreateTempSubdirectory("rask-ts-dts-");
        try
        {
            var source = Path.Combine(directory.FullName, "Card.ts");
            File.WriteAllText(source, """
                                      export function width(el: HTMLElement | null) { return el ? 1 : 0; }
                                      export class Chart { constructor(el: HTMLElement) {} draw(data: number[]): void {} }
                                      """);

            var (exitCode, output) = PinnedTools.Run(
                PinnedTools.Resolve("tsgo"),
                $"\"{source}\" --rootDir \"{directory.FullName}\" --outDir \"{directory.FullName}/out\" "
                + "--target es2020 --module esnext --noCheck --declaration --ignoreConfig");

            var declarations = File.ReadAllText(Path.Combine(directory.FullName, "out", "Card.d.ts"));
            Assert.True(exitCode == 0, output);
            // Inferred, and as a literal union — why TypeMapper folds `0 | 1` into a double.
            Assert.Contains("export declare function width(el: HTMLElement | null): 0 | 1;", declarations);
            Assert.Contains("export declare class Chart {", declarations);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void An_arrow_const_and_a_tuple_return_are_declared_in_the_forms_the_generator_reads()
    {
        var directory = Directory.CreateTempSubdirectory("rask-ts-dts-");
        try
        {
            var source = Path.Combine(directory.FullName, "Card.ts");
            File.WriteAllText(source, """
                                      export function pair(): [number, string] { return [1, "a"]; }
                                      export const double = (x: number) => x * 2;
                                      """);

            var (exitCode, output) = PinnedTools.Run(
                PinnedTools.Resolve("tsgo"),
                $"\"{source}\" --rootDir \"{directory.FullName}\" --outDir \"{directory.FullName}/out\" "
                + "--target es2020 --module esnext --noCheck --declaration --ignoreConfig");

            var declarations = File.ReadAllText(Path.Combine(directory.FullName, "out", "Card.d.ts"));
            var script = File.ReadAllText(Path.Combine(directory.FullName, "out", "Card.js"));
            Assert.True(exitCode == 0, output);
            Assert.Contains("export declare function pair(): [number, string];", declarations);
            Assert.Contains("export declare const double: (x: number) => number;", declarations);
            // Inline, so ScopedAssetRegistry's `export const NAME =` pattern finds it.
            Assert.Contains("export const double = (x) => x * 2;", script);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
