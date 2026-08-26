using Rask.Spa.Tasks;

namespace Rask.Spa.Tasks.Tests;

/// <summary>
///     The hand-off between the two halves of the front-end pipeline: the generator writes the
///     TypeScript into the assembly as a constant, and the build task reads it back out.
/// </summary>
/// <remarks>
///     Deliberately end-to-end rather than against a hand-written fixture. The task finds the constants
///     by looking up a namespace, a type name and two field names as strings — nothing in either
///     compiler relates the two sides, so a rename on either would compile cleanly and silently stop
///     the front end regenerating. Running the real generator is what makes that a failing test.
/// </remarks>
public class GeneratedTypeScriptTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "rask-spa-tasks-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private const string Contracts = """
        using System;
        using Rask.Cqrs;

        namespace Shop;

        public sealed record Order(Guid Id, DateTimeOffset PlacedAt, DateOnly DeliverBy);

        public sealed record GetOrder(Guid Id) : IQuery<Order>;
        """;

    [Fact]
    public void The_task_reads_what_the_generator_wrote()
    {
        var assembly = TestCompilation.Emit(Contracts, _directory);

        var constants = GeneratedTypeScript.Read(assembly);

        Assert.True(constants.ContainsKey("Contracts"), "The generator emitted no Contracts constant.");
        Assert.True(constants.ContainsKey("Messages"), "The generator emitted no Messages constant.");
        Assert.Contains("placedAt: Date;", constants["Contracts"], StringComparison.Ordinal);
        Assert.Contains("deliverBy: DateOnly;", constants["Contracts"], StringComparison.Ordinal);
        Assert.Contains("export const getOrder = message<", constants["Messages"], StringComparison.Ordinal);

        // The read has to reach the END of the constant, not merely start in the right place. Half a
        // TypeScript file still parses far enough to look plausible, and the first thing lost is the
        // tail of the file — the message factories, which is the half a front end imports.
        Assert.EndsWith("} as const;\n", constants["Contracts"], StringComparison.Ordinal);
        Assert.EndsWith("});\n", constants["Messages"].TrimEnd() + "\n", StringComparison.Ordinal);
    }

    [Fact]
    public void An_assembly_that_did_not_opt_in_carries_nothing()
    {
        // The whole front-end contract surface as a string literal is not something an in-process app
        // should pay for in its assembly, so the flag has to actually gate it.
        var assembly = TestCompilation.Emit(Contracts, _directory, emitTypeScript: false);

        Assert.Empty(GeneratedTypeScript.Read(assembly));
    }

    [Fact]
    public void An_assembly_with_no_contracts_at_all_is_not_an_error()
    {
        var assembly = TestCompilation.Emit("namespace Empty; public sealed class Nothing;", _directory);

        Assert.Empty(GeneratedTypeScript.Read(assembly));
    }

    [Fact]
    public void An_unchanged_file_is_not_rewritten()
    {
        // Load-bearing, not an optimisation: these files sit inside the front end's source tree with
        // the bundler's watcher on them, and they are also MSBuild inputs to the bundler run. Touching
        // them every build makes a watch build re-trigger itself.
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "contracts.ts");

        Assert.True(GeneratedTypeScript.WriteIfDifferent(path, "export type A = string;"));
        var stamp = File.GetLastWriteTimeUtc(path);

        Assert.False(GeneratedTypeScript.WriteIfDifferent(path, "export type A = string;"));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));

        Assert.True(GeneratedTypeScript.WriteIfDifferent(path, "export type A = number;"));
        Assert.Equal("export type A = number;", File.ReadAllText(path));
    }

    [Fact]
    public void Writing_creates_the_directory()
    {
        // The generated directory lives inside the client and is gitignored, so a fresh clone reaches
        // this with nothing there.
        var path = Path.Combine(_directory, "src", "rask", "contracts.ts");

        Assert.True(GeneratedTypeScript.WriteIfDifferent(path, "export {};"));
        Assert.True(File.Exists(path));
    }
}
