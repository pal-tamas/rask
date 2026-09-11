namespace Rask.TypeScript.Tasks.Tests;

/// <summary>
///     Holds what Rask DevTools adds to the shipped client runtimes to a byte budget.
/// </summary>
/// <remarks>
///     <para>
///         Every Release page loads <c>rask.js</c> or <c>rask.wasm.js</c>, and both carry the devtools' frame hooks:
///         one read of <c>window.__raskDevtoolsHook</c> at each send, receive and commit site. They were accepted on
///         the condition that they stay small and inert, and this is that condition written down. Loading the
///         devtools is deliberately NOT in the runtimes — the server writes the tag — which the second assertion
///         pins.
///     </para>
///     <para>
///         The cost is measured, not estimated. Each runtime is bundled twice with the pinned esbuild, with the
///         flags its project builds it with: once as shipped, and once with the hook defined as <c>undefined</c>,
///         which makes every hook branch dead code for the minifier to drop. The difference is what the hooks cost.
///         The stripped bundle is checked for the hook first, so a rename that stops the stripping cannot pass as a
///         zero-byte seam.
///     </para>
/// </remarks>
public class ClientRuntimeSeamBudgetTests
{
    /// <summary>Minified bytes the devtools hooks may add to one runtime.</summary>
    /// <remarks>Measured on 2026-09-11 at 184 (Server) and 185 (WASM) bytes, for four hook sites each.</remarks>
    private const int BudgetBytes = 250;

    public static TheoryData<string, string> Runtimes => new()
    {
        { "src/Rask.Server/Resources/rask.ts", "--format=iife --target=es2019" },
        { "src/Rask.Wasm/Resources/rask.wasm.ts", "--format=esm --target=es2020" },
    };

    [Theory]
    [MemberData(nameof(Runtimes))]
    public void The_devtools_hooks_stay_within_their_budget(string entry, string flags)
    {
        var esbuild = PinnedTools.Resolve("esbuild");
        var source = Path.Combine(PinnedTools.RepositoryRoot(), entry);
        var output = Directory.CreateTempSubdirectory("rask-seams-");
        try
        {
            var shipped = Bundle(esbuild, source, flags, Path.Combine(output.FullName, "shipped.js"));
            var stripped = Bundle(
                esbuild,
                source,
                flags + " --define:window.__raskDevtoolsHook=undefined",
                Path.Combine(output.FullName, "stripped.js"));

            // The hooks are there to be measured, and the stripped build really lost every one of them.
            Assert.Contains("__raskDevtoolsHook", shipped, StringComparison.Ordinal);
            Assert.DoesNotContain("__raskDevtoolsHook", stripped, StringComparison.Ordinal);

            var cost = shipped.Length - stripped.Length;
            Assert.True(
                cost <= BudgetBytes,
                $"The devtools hooks add {cost} minified bytes to {entry}, over the {BudgetBytes}-byte budget every "
                + "Release page pays. Move the new code into the devtools host script, which only a Debug page in "
                + "Development loads.");
        }
        finally
        {
            output.Delete(recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(Runtimes))]
    public void The_runtime_has_no_code_that_loads_the_devtools(string entry, string flags)
    {
        _ = flags;

        // The host script's route in a runtime would mean the runtime loads it — code every Release page carries.
        var source = File.ReadAllText(Path.Combine(PinnedTools.RepositoryRoot(), entry));

        Assert.DoesNotContain("_rask-devtools", source, StringComparison.Ordinal);
        Assert.DoesNotContain("__raskDevtoolsHost", source, StringComparison.Ordinal);
    }

    private static string Bundle(string esbuild, string source, string flags, string outfile)
    {
        var (exitCode, log) = PinnedTools.Run(
            esbuild, $"\"{source}\" --bundle {flags} --minify --log-level=warning --outfile=\"{outfile}\"");

        Assert.True(exitCode == 0, $"esbuild could not bundle {source}:{Environment.NewLine}{log}");
        return File.ReadAllText(outfile);
    }
}
