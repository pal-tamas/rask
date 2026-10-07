using System.Text.RegularExpressions;

namespace Rask.UiTests;

/// <summary>
///     The kit writes status text in the <c>-ink</c> tokens, never in daisyUI's status colours.
/// </summary>
/// <remarks>
///     <c>success</c>, <c>warning</c>, <c>error</c> and <c>info</c> are SURFACE colours. Read as text on
///     <c>base-100</c> they fail WCAG AA on most palettes — <c>ui.css</c> measures success at 1.95:1 on
///     white — which is why the <c>--color-ui-*-ink</c> tokens exist. A class name compiles whatever it
///     says, so the raw spelling came back in six components after the tokens were written.
/// </remarks>
public sealed partial class StatusTextUsesInkTests
{
    // Keyed by file as well as token, so an exemption cannot spread. The toast is the one place the raw
    // colours are right: its ground is the near-black `ui-ink`, where the light-ground inks invert.
    private static readonly HashSet<string> OnADarkGround = new(StringComparer.Ordinal)
    {
        "UiToast.cs:text-warning",
        "UiToast.cs:text-success",
    };

    [Fact]
    public void No_kit_component_writes_a_status_colour_as_text()
    {
        var kit = Path.Combine(RepoRoot.FullPath, "src", "Rask.Ui");

        var offenders = Directory
            .EnumerateFiles(kit, "*.cs", SearchOption.TopDirectoryOnly)
            .SelectMany(file => File.ReadLines(file)
                // A comment may say what the old spelling was; only code can write it.
                .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                .SelectMany(line => RawStatusText().Matches(line))
                .Select(match => $"{Path.GetFileName(file)}:{match.Value}"))
            .Where(offender => !OnADarkGround.Contains(offender))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These write a daisyUI status colour as text, which fails AA on base-100. Use the text-ui-*-ink "
            + "token (UiClassNames.ValueTone):\n  " + string.Join("\n  ", offenders));
    }

    [GeneratedRegex(@"(?<![\w:/-])text-(?:success|warning|error|info)(?![\w-])")]
    private static partial Regex RawStatusText();
}
