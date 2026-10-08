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

    // The owner has decided the console may use Tailwind, and it has a compiled sheet of its own now. Until the
    // rule itself is lifted, exactly these calls: the badge's long token, and what a queue row's detail modal
    // lays its content out with — Flux's modal spaces nothing, as Flux's examples do it with utilities.
    private static readonly string[] Allowed =
    [
        "Ui.Badge.Key(s.Key).Class(\"font-mono max-w-full break-all whitespace-normal!\")",
        "Div.Class(\"space-y-6\")[",
        "Div.Class(\"flex flex-wrap gap-2 sm:justify-end\")[actions]",
        // The frame's grounds and hairlines, which Flux's layouts leave to the page: the console's own sheet carries them.
        ".Class(\"border-e border-zinc-200 bg-zinc-50 dark:border-zinc-700 dark:bg-zinc-900\")[",
        "Ui.Header.Class(\"border-b border-zinc-200 bg-zinc-50 dark:border-zinc-700 dark:bg-zinc-900\")[",
    ];

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
                if (line.StartsWith("//", StringComparison.Ordinal)
                    || Allowed.Any(allowed => line.StartsWith(allowed, StringComparison.Ordinal))
                    || !ClassString.IsMatch(line))
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
    public void The_consoles_only_stylesheet_source_is_the_entry_that_takes_the_kit_in()
    {
        // One file, and it defines nothing: the kit's Tailwind sources, compiled by this project's build so
        // a class a page here writes is in the same sheet as the kit's. A second file, or a rule in this
        // one, would be a vocabulary of the console's own again.
        var styles = Path.Combine(PackageSourcePath(), "Styles");
        var sources = Directory.EnumerateFiles(styles, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(styles, f).Replace('\\', '/'))
            .Where(f => !f.StartsWith("vendor/", StringComparison.Ordinal)) // written by the build, not committed
            .ToArray();

        Assert.Equal(["dashboard.css"], sources);

        var entry = Regex.Replace(File.ReadAllText(Path.Combine(styles, "dashboard.css")), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        Assert.Contains("@import \"./vendor/rask-ui.kit.css\";", entry, StringComparison.Ordinal);
        Assert.Contains("@source \"./vendor/rask-ui.classes.txt\";", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("{", entry, StringComparison.Ordinal);
    }

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
        var page = Page.RenderDocument(RaskDashboardShell).Html;

        Assert.Contains("data-rask-ui", page, StringComparison.Ordinal);
        Assert.Contains("data-theme=\"light\"", page, StringComparison.Ordinal);
    }

    /// <summary>The console's only sheet is the kit's, so its document asks the kit for a reset and a ground.</summary>
    [Fact]
    public void The_document_element_says_the_kit_is_its_only_sheet()
    {
        var page = Page.RenderDocument(RaskDashboardShell).Html;

        Assert.Contains(UiStylesheet.DocumentAttribute, page, StringComparison.Ordinal);
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
