using System.Xml.Linq;

namespace Rask.TypeScript.Tasks.Tests;

/// <summary>
///     Pins <c>RaskScopedTsRemoveComments</c> in <c>Rask.Core.targets</c>: a Release build strips comments
///     from the emitted scoped JavaScript, a Debug build keeps them, and the property overrides both.
/// </summary>
/// <remarks>
///     <para>
///         Evaluated by real MSBuild rather than asserted on the file's text, because the text is where
///         this broke first: the explanatory comment originally quoted the flag with its leading dashes,
///         which XML forbids inside a comment, so every build importing the file failed to load it. A
///         string assertion over that text would have passed.
///     </para>
///     <para>
///         Evaluation only (<c>-getProperty</c>), so no target runs, nothing is downloaded, and the cost is
///         one short MSBuild process per row. That tsgo keeps the inline export form under the flag is
///         pinned separately, by <c>Tsgo_Emit_RemoveComments_PreservesTheInlineExportForm</c>.
///     </para>
/// </remarks>
public sealed class ScopedTypeScriptRemoveCommentsTests
{
    private static readonly string _targets =
        Path.Combine(PinnedTools.RepositoryRoot(), "src", "Rask.Core", "build", "Rask.Core.targets");

    [Theory]
    [InlineData("-p:Configuration=Release", " --removeComments")]
    [InlineData("-p:Configuration=Debug", "")]
    [InlineData("-p:Configuration=Release -p:RaskScopedTsRemoveComments=false", "")]
    [InlineData("-p:Configuration=Debug -p:RaskScopedTsRemoveComments=true", " --removeComments")]
    public void TheCompilerArgument_FollowsTheConfiguration_AndTheOverride(string properties, string expected)
    {
        var directory = Directory.CreateTempSubdirectory("rask-ts-comments-");
        try
        {
            var probe = Path.Combine(directory.FullName, "probe.proj");
            File.WriteAllText(probe, $"""<Project><Import Project="{_targets}"/></Project>""");

            var (exitCode, output) = PinnedTools.Run(
                Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                $"msbuild \"{probe}\" {properties} -getProperty:_RaskScopedTsRemoveCommentsArg");

            Assert.True(exitCode == 0, output);
            Assert.Equal(expected, output.TrimEnd('\r', '\n'));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void TheScopedCompile_PassesTheArgument()
    {
        // The property is inert unless the one tsgo invocation for scoped assets actually splices it in.
        var command = XDocument.Load(_targets)
            .Descendants()
            .Where(e => e.Name.LocalName == "Exec")
            .Select(e => (string?)e.Attribute("Command") ?? string.Empty)
            .Single(c => c.Contains("_RaskScopedTsOutDir", StringComparison.Ordinal));

        Assert.Contains("$(_RaskScopedTsRemoveCommentsArg)", command, StringComparison.Ordinal);
    }

    [Fact]
    public void FlippingAnOption_IsNotJudgedUpToDate()
    {
        // Inputs sees files only, so the options that shape the emit are written to a stamp the compile
        // lists as an input. Without it, `-c Release -p:RaskScopedTsRemoveComments=false` over a built tree
        // is judged up to date and ships the previous emit.
        var elements = XDocument.Load(_targets).Descendants().ToList();
        var compile = elements.Single(e =>
            e.Name.LocalName == "Target" && (string?)e.Attribute("Name") == "_RaskCompileScopedTs");
        var stamp = elements.Single(e =>
            e.Name.LocalName == "Target" && (string?)e.Attribute("Name") == "_RaskStampScopedTsOptions");
        var lines = (string?)stamp.Descendants().Single(e => e.Name.LocalName == "WriteLinesToFile").Attribute("Lines");

        Assert.Contains("$(_RaskScopedTsOptionsStamp)", (string?)compile.Attribute("Inputs"), StringComparison.Ordinal);
        Assert.Contains("_RaskStampScopedTsOptions", (string?)compile.Attribute("DependsOnTargets"), StringComparison.Ordinal);
        Assert.Contains("$(RaskScopedTsRemoveComments)", lines, StringComparison.Ordinal);
        Assert.Contains("$(RaskScopedTsSourceMap)", lines, StringComparison.Ordinal);
        Assert.Contains("$(RaskScopedTsTarget)", lines, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-p:Configuration=Debug", "true")]
    [InlineData("-p:Configuration=Release", "false")]
    [InlineData("-p:Configuration=Release -p:RaskScopedTsSourceMap=true", "true")]
    [InlineData("-p:Configuration=Debug -p:RaskScopedTsSourceMap=false", "false")]
    public void SourceMaps_AreDebugOnly_AndOverridable(string properties, string expected)
    {
        // #1073: a map is for a developer's debugger; a Release bundle a visitor downloads carries none.
        var directory = Directory.CreateTempSubdirectory("rask-ts-sourcemap-");
        try
        {
            var probe = Path.Combine(directory.FullName, "probe.proj");
            File.WriteAllText(probe, $"""<Project><Import Project="{_targets}"/></Project>""");

            var (exitCode, output) = PinnedTools.Run(
                Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                $"msbuild \"{probe}\" {properties} -getProperty:RaskScopedTsSourceMap");

            Assert.True(exitCode == 0, output);
            Assert.Equal(expected, output.TrimEnd('\r', '\n'));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void TheScopedCompile_InlinesTheMapWithASourceRootThatIsAFileUrl()
    {
        // Inline, so the map rides the compiled text the generator already embeds; sourceRoot as a file URL, so the
        // map's sources name the .ts on this machine wherever the bundle's map is served from.
        var elements = XDocument.Load(_targets).Descendants().ToList();
        var command = elements
            .Where(e => e.Name.LocalName == "Exec")
            .Select(e => (string?)e.Attribute("Command") ?? string.Empty)
            .Single(c => c.Contains("_RaskScopedTsOutDir", StringComparison.Ordinal));
        var arg = elements.Single(e => e.Name.LocalName == "_RaskScopedTsSourceMapArg").Value;

        Assert.Contains("$(_RaskScopedTsSourceMapArg)", command, StringComparison.Ordinal);
        Assert.Contains("--inlineSourceMap --inlineSources --sourceRoot", arg, StringComparison.Ordinal);
        Assert.Contains("[System.Uri]::new(", arg, StringComparison.Ordinal);
        Assert.Contains("AbsoluteUri", arg, StringComparison.Ordinal);
    }
}
