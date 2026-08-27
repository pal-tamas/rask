using System.Text.RegularExpressions;
using Xunit;

namespace Rask.Cli.Tests;

/// <summary>
///     Every E2E class in this assembly must be selected by some gate's <c>--filter</c>.
/// </summary>
/// <remarks>
///     <para>
///         A class no filter matches is not reported as skipped or missing — it simply never runs, while
///         the gate prints that it passed. That is the most expensive shape of failure this repo has: a
///         test that exists, is correct, is believed to be running, and is not.
///     </para>
///     <para>
///         It has already happened. The CLI build gate named suffixes (<c>~BuildE2ETests</c>), and
///         <c>TailwindPublishE2ETests</c> — written specifically to catch a stylesheet that never reached
///         the publish output — matched none of them and was executed by nothing at all.
///     </para>
///     <para>
///         Every gate is read, not just one, because several E2E classes belong to a script of their own
///         (watch, deploy). Asking only about one gate would report those as orphaned and train everyone
///         to ignore the answer.
///     </para>
/// </remarks>
public sealed class CliGateFilterTests
{
    [Fact]
    public void Every_e2e_class_is_selected_by_some_gate_filter()
    {
        var scripts = Directory.GetFiles(
            Path.Combine(CliBuildE2E.FindRepoRoot(), "scripts"), "run-*.sh");

        Assert.NotEmpty(scripts);

        var selectors = scripts
            .SelectMany(script => Regex.Matches(File.ReadAllText(script), @"--filter\s+""(?<f>[^""]+)"""))
            .SelectMany(m => m.Groups["f"].Value.Split('|', StringSplitOptions.RemoveEmptyEntries))
            .Select(part => part.Trim())
            // Only the one form the gates use. A filter this cannot parse is left out rather than
            // silently treated as matching everything, which would make this test vacuous.
            .Where(part => part.StartsWith("FullyQualifiedName~", StringComparison.Ordinal))
            .Select(part => part["FullyQualifiedName~".Length..])
            .ToArray();

        Assert.NotEmpty(selectors);

        var unreachable = typeof(CliGateFilterTests).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && t.Name.EndsWith("E2ETests", StringComparison.Ordinal))
            .Select(t => t.FullName!)
            .Where(name => !selectors.Any(s => name.Contains(s, StringComparison.Ordinal)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unreachable.Length == 0,
            $"no gate's --filter selects: {string.Join(", ", unreachable)}. They will never run, and "
            + "every gate will keep reporting success. Widen a --filter under scripts/, or rename the "
            + "class so an existing one matches.");
    }
}
