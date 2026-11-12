using System.Text.RegularExpressions;

namespace Rask.UiTests;

/// <summary>
///     How <c>Rask.Ui.targets</c> finds the kit's compiled stylesheet, and why it must not guess.
/// </summary>
/// <remarks>
///     <para>
///         The in-repo fallback looked for <c>obj/net11.0/ui.generated.css</c>, a literal path.
///         <c>Rask.Ui</c> multi-targets <c>net11.0;net11.0-browser</c>, so that file exists only where
///         something has built the <c>net11.0</c> face — which a developer's machine always has and a
///         clean CI checkout publishing a browser-WASM app never does.
///     </para>
///     <para>
///         <b>It shipped rask.sh entirely unstyled.</b> Every colour the kit's components write
///         lives in that sheet, so with it missing every colour token resolved to nothing: a page that is
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
    public void The_in_repo_fallback_does_not_name_a_target_framework()
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
    public void It_resolves_against_the_consumers_own_target_framework()
    {
        // First choice, because it is the matching sheet rather than merely a present one.
        Assert.Contains(
            "obj/$(TargetFramework)/ui.generated.css", _targets, StringComparison.Ordinal);
    }

    [Fact]
    public void It_falls_back_to_whichever_face_the_kit_has_built()
    {
        // Second choice: the generated CSS is Tailwind's scan of the kit's own sources and does not vary
        // by target, so any built face is the right content. Without this a consumer whose TFM the kit
        // has not built is back to the silent-grey failure.
        Assert.Contains("obj/*/ui.generated.css", _targets, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_stylesheet_FAILS_the_build()
    {
        // It warned. The warning ran green through CI and deployed an unstyled site, which is the whole
        // argument: this target only runs when the project asked for the sheet with
        // RaskUiWriteStylesheet=true, so "asked for it and it is not there" has no benign reading.
        var block = _targets[_targets.IndexOf("RaskUiWriteStylesheet\"", StringComparison.Ordinal)..];

        Assert.Contains("<Error Condition=", block, StringComparison.Ordinal);
        Assert.DoesNotContain("<Warning Condition=", block, StringComparison.Ordinal);
    }

    [Fact]
    public void The_packaged_sheet_is_collected_by_the_outer_build()
    {
        // The sheet the targets above look for in a PACKAGE has to get into that package, and for a
        // long time it did not.
        //
        // This project multi-targets, so `dotnet pack` generates the nuspec in the outer, TFM-less
        // build — while the target that adds `build/ui.css` runs only in the inner ones, because it
        // needs RaskTailwindOutput, which is deliberately unset outside them. An item added during an
        // inner build is not in the outer build's collection, so every package this project produced
        // shipped without the file its own targets then failed to find.
        //
        // Invisible from inside this repository: the only consumer that copies the sheet is here, and
        // a ProjectReference reads it straight out of obj/ without ever looking in a package.
        var csproj = File.ReadAllText(Path.Combine(RepoRoot.FullPath, "src", "Rask.Ui", "Rask.Ui.csproj"));

        Assert.Contains("Condition=\" '$(TargetFramework)' == '' \"", csproj, StringComparison.Ordinal);
        Assert.Contains("BeforeTargets=\"_GetPackageFiles\"", csproj, StringComparison.Ordinal);
        Assert.Contains("PackagePath=\"build/ui.css\"", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void The_sheet_is_looked_for_only_after_the_project_references_are_built()
    {
        // #1315: RaskUiWriteStylesheet hooks BeforeBuild, which runs ahead of the build's own reference resolution.
        // On a clean checkout an in-repo ProjectReference to the kit had not been built when the sheet was looked
        // for, so its obj/<tfm>/ui.generated.css was missing and the build failed (full.yml run 42608057980). A
        // lookup that runs early stays empty for the rest of the build — a target runs once — so the ordering is
        // on the lookup itself, not on any one target that uses it.
        var lookup = Regex.Match(_targets, "<Target Name=\"_RaskUiResolveStylesheet\"[^>]*>");

        Assert.True(lookup.Success, "the sheet's lookup target moved.");
        Assert.Matches("DependsOnTargets=\"ResolveProjectReferences;", lookup.Value);
        Assert.Matches("DependsOnTargets=\"[^\"]*_RaskUiResolveStylesheet", TargetTag("RaskUiWriteStylesheet"));
        Assert.Matches("DependsOnTargets=\"[^\"]*_RaskUiResolveStylesheet", TargetTag("_RaskUiAddStylesheetAsset"));
    }

    [Fact]
    public void A_browser_app_serves_the_sheet_its_pages_link_from_where_the_kit_keeps_it()
    {
        // #1315: the WASM host links UiStylesheet.Href() whenever the kit is on and not compiled into the app's own
        // sheet, and a browser app has no server to answer it — so the kit's sheet is one of its static web assets,
        // at exactly the address the host links.
        var target = _targets[_targets.IndexOf("<Target Name=\"_RaskUiAddStylesheetAsset\"", StringComparison.Ordinal)..];
        target = target[..(target.IndexOf("</Target>", StringComparison.Ordinal) + "</Target>".Length)];

        Assert.Contains("'$(UsingMicrosoftNETSdkWebAssembly)' == 'true'", target, StringComparison.Ordinal);
        Assert.Contains("'$(_RaskUiTailwind)' != 'true'", target, StringComparison.Ordinal);
        Assert.Contains($"RelativePath=\"{UiStylesheet.Path.TrimStart('/')}\"", target, StringComparison.Ordinal);
        Assert.Contains("<DefineStaticWebAssets", target, StringComparison.Ordinal);
        Assert.Contains("<Error Condition=", target, StringComparison.Ordinal);
    }

    private static string TargetTag(string name)
    {
        var tag = Regex.Match(_targets, $"<Target Name=\"{name}\"[^>]*>");
        Assert.True(tag.Success, $"the {name} target moved.");
        return tag.Value;
    }

    private static string ReadTargets()
    {
        var path = Path.Combine(RepoRoot.FullPath, "src", "Rask.Ui", "build", "Rask.Ui.targets");
        Assert.True(File.Exists(path), $"the kit's targets moved: {path}");

        // Comments stripped, because the file DOCUMENTS the old literal path as the bug it was — and a
        // test that reads prose would fail on the explanation of the thing it is checking for.
        return Regex.Replace(File.ReadAllText(path), "<!--.*?-->", string.Empty, RegexOptions.Singleline);
    }
}
