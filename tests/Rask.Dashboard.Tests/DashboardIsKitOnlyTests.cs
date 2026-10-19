using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Rask.Core.Routing;
using Rask.Testing;

namespace Rask.Dashboard.Tests;

/// <summary>
///     The console is drawn with Flux UI's pieces from <c>Rask.Ui</c>, and with nothing else of the kit's.
/// </summary>
/// <remarks>
///     <para>
///     Every visible part of it is a Flux component: a table, a card, a heading, a badge. Its own layout — a grid of
///     cards, the gap between two buttons — is written in Tailwind beside them, which its own sheet compiles. The
///     kit's components that are not Flux's (the data grid, the metric, the detail list…) are on their way out of the
///     kit, and the console uses none of them.
///     </para>
///     <para>
///     Read from the source rather than the rendered markup: a page renders only the panels its batteries
///     registered, and a component that is never rendered in a test is still one the console depends on.
///     </para>
/// </remarks>
public sealed partial class DashboardIsKitOnlyTests : global::Rask.Core.RaskMarkup
{
    // `Ui.Table`, `Ui.IconName` — an entry or a type of the kit, named through its static class.
    private static readonly Regex KitMember = new(@"\bUi\.([A-Z][A-Za-z]*)\b", RegexOptions.Compiled);

    // `UiNavbarItem`, `UiPaginator` — a kit type named directly.
    private static readonly Regex KitType = new(@"\bUi([A-Z][A-Za-z]*)\b", RegexOptions.Compiled);

    // Flux's, but under no part of the snapshot's own (FluxConformanceTests.FluxUnlisted), and the dark-mode script
    // every Flux page's head carries (a helper there).
    private static readonly string[] FluxBesidesTheSnapshot = ["Navmenu", "NavmenuItem", "Spacer", "AppearanceScript"];

    // The kit's own vocabulary for things Flux says otherwise: daisyUI's tones, themes and breakpoints, and the
    // class lists that went with them.
    private static readonly string[] NotFlux = ["Tone", "ThemeName", "Breakpoint", "Styles", "Theme"];

    // Every paged or listed table: from its column heads to the end of them.
    private static readonly Regex TableHead = new(@"Ui\.TableColumns\[(.*?)\]\s*,\s*\r?\n\s*Ui\.TableRows", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex TableColumn = new(@"Ui\.TableColumn\b[^\r\n]*", RegexOptions.Compiled);

    [Fact]
    public void The_console_draws_with_Flux_pieces_only()
    {
        var flux = FluxParts();

        var offenders = CodeLines()
            .SelectMany(line => NotFluxNames(line.Text, flux).Select(name => $"{line.Where}: Ui.{name} in {line.Text.Trim()}"))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "The console names kit pieces that are not Flux UI's — draw it with Flux's components instead:\n"
            + string.Join('\n', offenders));
    }

    [Fact]
    public void Every_table_gives_all_but_one_of_its_columns_a_width()
    {
        var tables = SourceFiles().SelectMany(file => TableHead.Matches(File.ReadAllText(file))).ToList();

        var loose = tables
            .Select(table => TableColumn.Matches(table.Groups[1].Value))
            .Where(columns => columns.Count(column => !column.Value.Contains(".Class(\"w-", StringComparison.Ordinal)) != 1)
            .Select(columns => columns[0].Value.Trim())
            .ToList();

        Assert.Equal(7, tables.Count);
        Assert.True(loose.Count == 0, "A table's columns shift with its rows:\n" + string.Join('\n', loose));
    }

    [Fact]
    public void The_scan_reads_the_pages_and_the_flux_catalogue()
    {
        var files = SourceFiles();

        var parts = FluxParts();

        // Vacuous-pass guards: a path that stopped resolving would find no files and no offenders.
        Assert.Contains(files, f => f.EndsWith("QueuePage.cs", StringComparison.Ordinal));
        Assert.Contains("TableColumn", parts);
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

        var entry = Regex.Replace(File.ReadAllText(Path.Combine(styles, "dashboard.css")), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        Assert.Equal(["dashboard.css"], sources);
        Assert.Contains("@import \"./vendor/rask-ui.kit.css\";", entry, StringComparison.Ordinal);
        Assert.Contains("@source \"./vendor/rask-ui.classes.txt\";", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("{", entry, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Dark mode is Flux's: a <c>dark</c> class on <c>&lt;html&gt;</c> from the operator's preference, never a
    ///     pinned daisyUI theme.
    /// </summary>
    [Fact]
    public async Task The_document_takes_fluxs_dark_mode_rather_than_a_pinned_theme()
    {
        // Development, because the fail-closed default denies everyone in Production and the layout that carries
        // the head sits behind that policy.
        await using var h = new DashboardHarness(Batteries.None, environment: Environments.Development);
        h.Get<RouteState>().Path = "/_rask/system";

        var page = Page.RenderDocument(RaskDashboardShell, h.Services).Html;

        var root = Regex.Match(page, "<html[^>]*>").Value;

        Assert.StartsWith("<html", root, StringComparison.Ordinal);
        Assert.DoesNotContain("data-theme", root, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"data-rask-ui(?!-)", root);
        Assert.Contains("classList.toggle('dark'", page, StringComparison.Ordinal);
    }

    /// <summary>The console's only sheet is its own, the kit compiled in, so its document asks the kit for a reset and a ground.</summary>
    [Fact]
    public void The_document_element_says_the_kit_is_its_only_sheet()
    {
        var page = Page.RenderDocument(RaskDashboardShell).Html;

        Assert.Contains(UiStylesheet.DocumentAttribute, page, StringComparison.Ordinal);
    }

    // A name is Flux's when the snapshot lists its part, or it is one of the few Flux pieces the snapshot does not
    // head. A type of the kit that is no component — an enum, the paginator, the stylesheet's constants — is a
    // value, not a piece drawn, except the daisyUI-era vocabulary in NotFlux.
    private static IEnumerable<string> NotFluxNames(string line, HashSet<string> flux)
    {
        var kit = typeof(UiTable).Assembly;
        var names = KitMember.Matches(line).Select(m => m.Groups[1].Value)
            .Concat(KitType.Matches(line).Select(m => m.Groups[1].Value))
            .Distinct(StringComparer.Ordinal);

        foreach (var name in names)
        {
            if (NotFlux.Contains(name, StringComparer.Ordinal))
            {
                yield return name;
                continue;
            }

            var component = kit.GetType($"Rask.Ui{name}") ?? kit.GetType($"Rask.Ui{name}`1") ?? kit.GetType($"Rask.Ui{name}`2");
            var drawn = component is not null && typeof(global::Rask.Core.Component).IsAssignableFrom(component);
            if (drawn && !flux.Contains(name) && !FluxBesidesTheSnapshot.Contains(name, StringComparer.Ordinal))
            {
                yield return name;
            }
        }
    }

    // flux:table.column -> TableColumn, flux:date-picker -> DatePicker, flux:icon.* -> Icon.
    private static HashSet<string> FluxParts()
    {
        using var snapshot = JsonDocument.Parse(File.ReadAllText(SnapshotPath()));
        return snapshot.RootElement.GetProperty("pages").EnumerateArray()
            .SelectMany(page => page.GetProperty("parts").EnumerateArray())
            .Select(part => part.GetProperty("name").GetString()!)
            .Select(name => string.Concat(name["flux:".Length..].Split('-', '.', '*').Where(w => w.Length > 0).Select(w => char.ToUpperInvariant(w[0]) + w[1..])))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<(string Where, string Text)> CodeLines() =>
        SourceFiles().SelectMany(file => File.ReadAllLines(file)
            .Select((text, i) => (Where: $"{Path.GetRelativePath(PackageSourcePath(), file)}:{i + 1}", Text: text))
            // Comments may name the thing they explain; only code draws it.
            .Where(line => !line.Text.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static List<string> SourceFiles()
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

    // Flux's catalogue as the kit's conformance gate reads it (declared in the csproj's RaskTestReads).
    private static string SnapshotPath([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "Rask.Ui.Tests", "Flux", "flux.snapshot.json"));
}
