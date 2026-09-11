using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Rask.Core.Routing;
using Rask.Testing;

namespace Rask.Dashboard.Tests;

/// <summary>
///     The console is drawn with <c>Rask.Ui</c> components and nothing else.
/// </summary>
/// <remarks>
///     <para>
///     A class string written in this package is a class no sheet can carry. Tailwind emits a utility only
///     where it can see the name in the source it scans, and the kit's sheet is compiled from the kit's
///     source — so a <c>.Class("mt-4")</c> here renders as nothing at all, with the build green and the
///     markup looking correct. The console used to paper over that with a second Tailwind build of its own,
///     which put two vocabularies on one page and two palettes under them.
///     </para>
///     <para>
///     So the rule is enforced on the source: where a page needs something the kit cannot draw, the answer is
///     a typed step on a kit component, never a class written here.
///     </para>
/// </remarks>
public sealed partial class DashboardIsKitOnlyTests : global::Rask.Core.RaskMarkup
{
    private static readonly Regex ClassString = new(@"\.(Class|Style)\(|\bUiStyles\.", RegexOptions.Compiled);

    [Fact]
    public void The_console_writes_no_class_strings()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimStart();

                // Comments may name the thing they explain; only code writes a class.
                if (line.StartsWith("//", StringComparison.Ordinal) || !ClassString.IsMatch(line))
                {
                    continue;
                }

                offenders.Add($"{Path.GetRelativePath(PackageSourcePath(), file)}:{i + 1}: {line}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "The console writes class strings, which no stylesheet can carry — add or extend a Rask.Ui "
            + "component instead:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void The_scan_reads_the_pages()
    {
        // Vacuous-pass guard: a path that stopped resolving would find no files and no offenders.
        Assert.Contains(SourceFiles(), f => f.EndsWith("QueuePage.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void The_console_has_no_stylesheet_source() =>
        Assert.False(Directory.Exists(Path.Combine(PackageSourcePath(), "Styles")));

    /// <summary>
    ///     The theme scope reaches the document, and it names a theme.
    /// </summary>
    /// <remarks>
    ///     A scope with no <c>data-theme</c> matches daisyUI's <c>[data-rask-ui]:not([data-theme])</c>, which
    ///     follows <c>prefers-color-scheme</c> and repaints the console dark on an operator's dark-mode laptop.
    ///     And the frame's reset paints <c>&lt;body&gt;</c> from <c>--color-base-200</c>, which only exists
    ///     inside the scope — so the scope has to be on <c>&lt;html&gt;</c>, above the body, too.
    /// </remarks>
    [Fact]
    public void The_document_element_carries_the_theme_scope_and_names_the_theme()
    {
        var page = RaskTest.RenderDocument(RaskDashboardShell).Html;

        Assert.Contains("data-rask-ui", page, StringComparison.Ordinal);
        Assert.Contains("data-theme=\"light\"", page, StringComparison.Ordinal);
    }

    /// <summary>…and so does the shell inside it, which is the second element carrying the scope.</summary>
    /// <remarks>
    ///     <c>[data-rask-ui]:not([data-theme])</c> matches on the ELEMENT, so a <c>UiShell</c> with no theme
    ///     would re-declare <c>--color-base-*</c> dark for everything beneath it while the document stayed light.
    /// </remarks>
    [Fact]
    public async Task The_shell_inside_it_pins_the_same_theme()
    {
        await using var h = new DashboardHarness(environment: Environments.Development);
        h.Get<RouteState>().Path = "/_rask";

        var page = RaskTest.RenderDocument(RaskDashboardShell, h.Services);

        Assert.True(
            page.Exists(".rask-ops[data-rask-ui][data-theme=\"light\"]"),
            "UiShell renders the kit's theme scope with no theme named, so daisyUI falls back to "
            + "prefers-color-scheme for everything inside it.");
    }

    private static IEnumerable<string> SourceFiles()
    {
        var separator = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(PackageSourcePath(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                        && !f.Contains($"{separator}bin{separator}", StringComparison.Ordinal))
            .ToList();
    }

    // Resolved from the compiler rather than copied to the output directory: the files under test are the
    // ones the package compiles, not a build artifact of them.
    private static string PackageSourcePath([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "Rask.Dashboard"));
}
