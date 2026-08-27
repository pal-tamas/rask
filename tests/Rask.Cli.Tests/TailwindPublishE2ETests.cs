using Rask.Cli.Scaffolding;
using Xunit;

namespace Rask.Cli.Tests;

/// <summary>
///     Does the stylesheet Tailwind compiles actually reach the published output?
/// </summary>
/// <remarks>
///     <para>
///         Every other Tailwind gate stops at "it builds". That is the one thing this failure mode does not
///         disturb: <c>Rask.Tailwind</c> writes <c>wwwroot/css/app.css</c> from a target hooked at
///         <c>BeforeBuild</c>, which runs <b>after</b> the SDK has globbed <c>wwwroot/**</c> as Content at
///         evaluation time. On a clean build the file therefore is not an item, not a static web asset, and
///         never lands in <c>publish/wwwroot</c> — while the build itself succeeds and the compiler really
///         did emit the CSS. The app ships with a <c>&lt;link&gt;</c> to a 404 and renders unstyled.
///     </para>
///     <para>
///         A second build hides it, because by then the file is on disk from the first. That is what makes
///         it intermittent, and why it gets blamed on whatever else changed.
///     </para>
///     <para>
///         So this asserts on the <b>publish output</b>, into a directory that did not exist before the run.
///         <c>dotnet publish</c> does not clean its output, so a stale <c>app.css</c> from an earlier
///         publish would make a broken one look fine — the trap the E2E script already warns about for the
///         scoped-asset bake.
///     </para>
/// </remarks>
public sealed class TailwindPublishE2ETests
{
    [SkippableTheory]
    [InlineData("server")]
    [InlineData("wasm")]
    public async Task The_compiled_stylesheet_reaches_the_publish_output(string template)
    {
        Skip.IfNot(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        var name = "RTailwind" + template.Replace("-", "", StringComparison.Ordinal);
        var temp = Path.Combine(Path.GetTempPath(), "rask-tailwind-publish", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);

        try
        {
            var batteries = new ServerBatteries { Styling = Styling.Tailwind };
            var result = template == "wasm"
                ? ProjectGenerator.GenerateWasm(projectDir, name, auth: false, pwa: false, docker: false, version, batteries)
                : ProjectGenerator.GenerateServer(projectDir, name, batteries, version);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            // A directory that does not exist yet, so nothing can be left over from an earlier run.
            var publishDir = Path.Combine(temp, "published");

            var (exit, output) = await CliBuildE2E.RunDotnet(
                $"publish \"{Path.Combine(projectDir, name + ".csproj")}\" -c Release -o \"{publishDir}\" -m:1 -nodeReuse:false");

            Assert.True(exit == 0, $"[{template}] publish failed.{output}");

            var css = Path.Combine(publishDir, "wwwroot", "css", "app.css");
            Assert.True(
                File.Exists(css),
                $"[{template}] the build compiled Tailwind and the publish did not carry it: {css} is absent. "
                + "The app shell emits <link href=\"/css/app.css\">, so the published site renders with no "
                + "styles at all — and the build succeeded, which is why nothing else catches this.");

            // Present but empty would be the same bug wearing a hat: the compiler ran against the wrong
            // working directory and scanned nothing.
            Assert.True(
                new FileInfo(css).Length > 0,
                $"[{template}] {css} was published but is empty — Tailwind scanned no source.");
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); }
            catch (IOException) { /* best effort */ }
            catch (UnauthorizedAccessException) { /* best effort */ }
        }
    }
}
