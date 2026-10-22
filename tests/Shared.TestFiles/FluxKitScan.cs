using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rask.TestFiles;

/// <summary>
///     Reads a surface's C# for the kit pieces it names, and says which of them are not Flux UI's.
/// </summary>
/// <remarks>
///     Read from the source rather than the rendered markup: a piece that no test happens to render is still one the
///     surface depends on. The operator console and the devtools panel are both held to it.
/// </remarks>
internal static class FluxKitScan
{
    // `Ui.Table`, `Ui.IconName` — an entry or a type of the kit, named through its static class.
    private static readonly Regex KitMember = new(@"\bUi\.([A-Z][A-Za-z]*)\b", RegexOptions.Compiled);

    // `UiNavbarItem`, `UiPaginator` — a kit type named directly.
    private static readonly Regex KitType = new(@"\bUi([A-Z][A-Za-z]*)\b", RegexOptions.Compiled);

    // Flux's, but under no part of the snapshot's own (FluxConformanceTests.FluxUnlisted), and the dark-mode script
    // every Flux page's head carries (a helper there).
    private static readonly string[] FluxBesidesTheSnapshot = ["Navmenu", "NavmenuItem", "Spacer", "AppearanceScript"];

    // The kit's old vocabulary for things Flux says otherwise: tones, themes and breakpoints, and the class lists
    // that went with them. None may come back.
    private static readonly string[] NotFlux = ["Tone", "ThemeName", "Breakpoint", "Styles", "Theme"];

    /// <summary>Every kit piece the C# under <paramref name="sourceRoot" /> names that is not Flux's, with where.</summary>
    internal static List<string> Offenders(string sourceRoot, string snapshotPath)
    {
        var flux = FluxParts(snapshotPath);
        return CodeLines(sourceRoot)
            .SelectMany(line => NotFluxNames(line.Text, flux).Select(name => $"{line.Where}: Ui.{name} in {line.Text.Trim()}"))
            .ToList();
    }

    /// <summary>The C# files under <paramref name="sourceRoot" />, build output left out.</summary>
    internal static List<string> SourceFiles(string sourceRoot)
    {
        var separator = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                        && !f.Contains($"{separator}bin{separator}", StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>Flux's parts as the kit names them: flux:table.column → TableColumn, flux:icon.* → Icon.</summary>
    internal static HashSet<string> FluxParts(string snapshotPath)
    {
        using var snapshot = JsonDocument.Parse(File.ReadAllText(snapshotPath));
        return snapshot.RootElement.GetProperty("pages").EnumerateArray()
            .SelectMany(page => page.GetProperty("parts").EnumerateArray())
            .Select(part => part.GetProperty("name").GetString()!)
            .Select(name => string.Concat(name["flux:".Length..].Split('-', '.', '*').Where(w => w.Length > 0).Select(w => char.ToUpperInvariant(w[0]) + w[1..])))
            .ToHashSet(StringComparer.Ordinal);
    }

    // A name is Flux's when the snapshot lists its part, or it is one of the few Flux pieces the snapshot does not
    // head. A type of the kit that is no component — an enum, the paginator, the stylesheet's constants — is a
    // value, not a piece drawn, except the old vocabulary in NotFlux.
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

    private static IEnumerable<(string Where, string Text)> CodeLines(string sourceRoot) =>
        SourceFiles(sourceRoot).SelectMany(file => File.ReadAllLines(file)
            .Select((text, i) => (Where: $"{Path.GetRelativePath(sourceRoot, file)}:{i + 1}", Text: text))
            // Comments may name the thing they explain; only code draws it.
            .Where(line => !line.Text.TrimStart().StartsWith("//", StringComparison.Ordinal)));
}
