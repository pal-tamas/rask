using System.Text.Json;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     The scaffolded <c>tsconfig.json</c> and the build target that stages Rask's ambient declarations
///     agree on where those declarations land.
/// </summary>
/// <remarks>
///     <para>
///         Two files in different languages have to name one path: <c>Rask.Core.targets</c> copies
///         <c>rask-globals.d.ts</c> into <c>obj/rask/types</c>, and the tsconfig <c>include</c>s it. If
///         either moves, nothing fails — the build still compiles the scoped asset exactly as before,
///         because the build passes tsgo the packaged copy by absolute path and never reads this file.
///         Only the editor notices, by silently losing every completion and every error on
///         <c>window.Rask</c> and <c>window.DotNet</c>.
///     </para>
///     <para>
///         That is the whole reason this is a test: the failure has no build output, no diagnostic and
///         no runtime symptom. It shows up as a developer wondering why their scoped TypeScript is
///         unchecked, months later.
///     </para>
/// </remarks>
public class ScaffoldedTsConfigTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheTsConfigIncludesWhereTheBuildStagesTheAmbientDeclarations(bool wasm)
    {
        var files = Scaffold(wasm);

        Assert.True(files.TryGetValue("tsconfig.json", out var text), "rask new wrote no tsconfig.json.");

        using var document = JsonDocument.Parse(text!);
        var include = document.RootElement.GetProperty("include")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();

        var staged = StagedTypesDirectory();
        Assert.True(
            include.Any(i => i is not null && i.Replace('\\', '/').StartsWith(staged, StringComparison.Ordinal)),
            $"the tsconfig does not include '{staged}', where Rask.Core.targets stages rask-globals.d.ts. "
            + "The build is unaffected; the editor silently loses every type in it.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheTsConfigDoesNotEmit(bool wasm)
    {
        var files = Scaffold(wasm);

        using var document = JsonDocument.Parse(files["tsconfig.json"]);
        var options = document.RootElement.GetProperty("compilerOptions");

        // An editor or a stray `tsc` in this directory that decided to emit would write a .js beside
        // the .ts — which is RASK055, and a confusing way to meet it. tsgo writes the real output, to
        // obj/, with flags of its own.
        Assert.True(options.GetProperty("noEmit").GetBoolean(), "the scaffolded tsconfig must not emit.");

        // The same bar the framework holds itself to, and the reason the migration happened.
        Assert.True(options.GetProperty("strict").GetBoolean(), "the scaffolded tsconfig must be strict.");
    }

    /// <summary>The path <c>_RaskStageScopedTsTypes</c> copies into, read from the targets file itself.</summary>
    private static string StagedTypesDirectory()
    {
        var targets = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Rask.Core", "build", "Rask.Core.targets"));
        const string marker = "<_RaskScopedTsTypesDir>";

        var start = targets.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "_RaskScopedTsTypesDir is gone from Rask.Core.targets — this test has gone stale.");

        var value = targets[(start + marker.Length)..targets.IndexOf("</_RaskScopedTsTypesDir>", start, StringComparison.Ordinal)];

        // The property is an absolute path built from $(IntermediateOutputPath); what the tsconfig can
        // name is the project-relative tail, which is what both sides actually have to agree on.
        var tail = value[(value.IndexOf("IntermediateOutputPath)", StringComparison.Ordinal) + "IntermediateOutputPath)".Length)..];
        return "obj/" + tail[..tail.IndexOf('\'')].Trim('/');
    }

    private static Dictionary<string, string> Scaffold(bool wasm)
    {
        var root = Path.Combine(Path.GetTempPath(), "rask-tsconfig", Guid.NewGuid().ToString("N"));
        var result = wasm
            ? ProjectGenerator.GenerateWasm(root, "App", auth: false, pwa: false, docker: false, "1.0.0")
            : ProjectGenerator.GenerateServer(root, "App", new ServerBatteries(), "1.0.0");

        return result.Files.ToDictionary(
            f => Path.GetRelativePath(root, f.Path).Replace('\\', '/'),
            f => f.Content,
            StringComparer.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Rask.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
