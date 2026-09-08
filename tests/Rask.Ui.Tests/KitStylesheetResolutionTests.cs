using System.Text.RegularExpressions;

namespace Rask.Ui.Tests;

/// <summary>
///     How <c>Rask.Ui.targets</c> finds the kit's compiled stylesheet, and why it must not guess.
/// </summary>
/// <remarks>
///     <para>
///         The in-repo fallback looked for <c>obj/net10.0/ui.generated.css</c>, a literal path.
///         <c>Rask.Ui</c> multi-targets <c>net10.0;net10.0-browser</c>, so that file exists only where
///         something has built the <c>net10.0</c> face — which a developer's machine always has and a
///         clean CI checkout publishing a browser-WASM app never does.
///     </para>
///     <para>
///         <b>It shipped rask.sh entirely unstyled.</b> The base colours and the whole daisyUI palette
///         live in that sheet, so with it missing every colour token resolved to nothing: a page that is
///         structurally perfect, fully interactive, and completely grey. Locally every build, every
///         publish and the whole browser suite were green, because the file was already on disk from an
///         earlier build. The only trace was one line in a CI log.
///     </para>
///     <para>
///         Asserted against the targets file's text because there is no other seam. Running the target
///         in-repo builds <c>Rask.Ui</c> as a project reference first, which regenerates the sheet — so
///         the failing configuration cannot be reached from a test that builds anything.
///     </para>
/// </remarks>
public sealed class KitStylesheetResolutionTests
{
    private static readonly string _targets = ReadTargets();

    [Fact]
    public void TheInRepoFallbackDoesNotNameATargetFramework()
    {
        // The regression itself. Any literal TFM here is a path that is right on one machine and absent
        // on another, and the difference does not surface until a deploy.
        var literals = Regex.Matches(_targets, @"obj/net\d+\.\d+[a-z-]*/ui\.generated\.css");

        Assert.True(
            literals.Count == 0,
            "Rask.Ui.targets names a target framework literally: "
            + string.Join(", ", literals.Select(m => m.Value))
            + ". Rask.Ui multi-targets, so a literal path exists only for whichever face happened to be "
            + "built — which is how the published site shipped unstyled.");
    }

    [Fact]
    public void ItResolvesAgainstTheConsumersOwnTargetFramework()
    {
        // First choice, because it is the matching sheet rather than merely a present one.
        Assert.Contains(
            "obj/$(TargetFramework)/ui.generated.css", _targets, StringComparison.Ordinal);
    }

    [Fact]
    public void ItFallsBackToWhicheverFaceTheKitHasBuilt()
    {
        // Second choice: the generated CSS is Tailwind's scan of the kit's own sources and does not vary
        // by target, so any built face is the right content. Without this a consumer whose TFM the kit
        // has not built is back to the silent-grey failure.
        Assert.Contains("obj/*/ui.generated.css", _targets, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingStylesheetFAILSTheBuild()
    {
        // It warned. The warning ran green through CI and deployed an unstyled site, which is the whole
        // argument: this target only runs when the project asked for the sheet with
        // RaskUiWriteStylesheet=true, so "asked for it and it is not there" has no benign reading.
        var block = _targets[_targets.IndexOf("RaskUiWriteStylesheet\"", StringComparison.Ordinal)..];

        Assert.Contains("<Error Condition=", block, StringComparison.Ordinal);
        Assert.DoesNotContain("<Warning Condition=", block, StringComparison.Ordinal);
    }

    private static string ReadTargets()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, "src", "Rask.Ui", "build", "Rask.Ui.targets");
        Assert.True(File.Exists(path), $"the kit's targets moved: {path}");

        // Comments stripped, because the file DOCUMENTS the old literal path as the bug it was — and a
        // test that reads prose would fail on the explanation of the thing it is checking for.
        return Regex.Replace(File.ReadAllText(path), "<!--.*?-->", string.Empty, RegexOptions.Singleline);
    }
}
