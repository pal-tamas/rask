using System.Text.RegularExpressions;

namespace Rask.UiTests;

/// <summary>
///     The kit's typeface: the compiled sheet draws in Inter, and asks for exactly the files the build writes.
/// </summary>
/// <remarks>
///     Three places name the same two files — the <c>@font-face</c> rules in Styles/ui.css, the files in
///     src/Rask.Ui/Fonts the package carries, and the list build/Rask.Ui.targets copies into an app's wwwroot —
///     and nothing at runtime notices when one of them drifts: a face the page cannot load is skipped for the next
///     family in the stack, and the kit quietly draws in the system font again. So the three are held to one.
/// </remarks>
public sealed partial class KitTypefaceTests
{
    private static readonly string Kit = Path.Combine(RepoRoot.FullPath, "src", "Rask.Ui");

    [Fact]
    public void The_default_sans_is_Inter_with_the_system_stack_after_it()
    {
        var sans = FontSans().Match(UiStylesheet.Css);

        Assert.True(sans.Success, "the compiled sheet defines no --font-sans");
        Assert.StartsWith("\"Inter\",", sans.Groups["value"].Value, StringComparison.Ordinal);
        Assert.Contains("sans-serif", sans.Groups["value"].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_face_the_sheet_asks_for_is_a_file_the_kit_ships_and_the_build_writes()
    {
        var targets = File.ReadAllText(Path.Combine(Kit, "build", "Rask.Ui.targets"));
        var urls = FontUrl().Matches(UiStylesheet.Css).Select(m => m.Groups["file"].Value).ToArray();

        // latin and latin-ext: the second is where Hungarian's ő and ű are.
        Assert.Equal(["inter-latin.woff2", "inter-latin-ext.woff2"], urls);

        foreach (var file in urls)
        {
            Assert.True(File.Exists(Path.Combine(Kit, "Fonts", file)), $"the sheet asks for /fonts/{file}, which the kit does not ship");
            Assert.Contains($"Include=\"wwwroot/fonts/{file}\"", targets, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_faces_travel_with_the_OFL_and_Inters_copyright()
    {
        var licence = File.ReadAllText(Path.Combine(Kit, "Fonts", "LICENSE.txt"));

        Assert.Contains("Copyright (c) 2016 The Inter Project Authors (https://github.com/rsms/inter)", licence, StringComparison.Ordinal);
        Assert.Contains("SIL OPEN FONT LICENSE Version 1.1", licence, StringComparison.Ordinal);
    }

    [GeneratedRegex("--font-sans:\\s*(?<value>[^;]+);")]
    private static partial Regex FontSans();

    [GeneratedRegex("@font-face\\s*\\{[^}]*src:\\s*url\\(\"?/fonts/(?<file>[^)\"]+)\"?\\)")]
    private static partial Regex FontUrl();
}
