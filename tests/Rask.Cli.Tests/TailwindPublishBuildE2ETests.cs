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
///         evaluation time. The build succeeds and the compiler really did emit the CSS; the only question
///         is whether the bytes are in the artifact.
///     </para>
///     <para>
///         <b>Where that actually bites, corrected.</b> For an APP — <c>Sdk.Web</c>, <c>Sdk.WebAssembly</c>,
///         the two cases below — it does not: static-web-asset discovery re-enumerates during the build and
///         picks the file up regardless of the glob. That was verified empirically on both SDKs, and an
///         earlier fix written on the opposite belief was binned when a publish-asserting test passed
///         against unfixed main.
///     </para>
///     <para>
///         For a RAZOR CLASS LIBRARY it bites completely, and that is the case
///         <see cref="A_class_librarys_compiled_stylesheet_reaches_the_consuming_apps_publish"/> covers.
///         An RCL's assets come from the evaluated <c>@(Content)</c> with no second pass, so a file
///         generated at <c>BeforeBuild</c> never enters its manifest. This class shipped for weeks
///         asserting the wrong half of the mechanism, which is why the docs showcase served a 404 for its
///         stylesheet and nothing went red.
///     </para>
///     <para>
///         A second build hides it, because by then the file is on disk from the first. That is what makes
///         it look intermittent, and why it gets blamed on whatever else changed.
///     </para>
///     <para>
///         So this asserts on the <b>publish output</b>, into a directory that did not exist before the run.
///         <c>dotnet publish</c> does not clean its output, so a stale <c>app.css</c> from an earlier
///         publish would make a broken one look fine — the trap the E2E script already warns about for the
///         scoped-asset bake.
///     </para>
/// </remarks>
public sealed class TailwindPublishBuildE2ETests
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
            var batteries = new ServerBatteries();
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

    /// <summary>
    ///     The same question for a RAZOR CLASS LIBRARY, which is the shape that actually broke.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The two cases above have never failed, and cannot: for an app (<c>Sdk.Web</c>,
    ///         <c>Sdk.WebAssembly</c>) static-web-asset discovery re-enumerates <c>wwwroot</c> during the
    ///         build, so a file written at <c>BeforeBuild</c> is picked up despite missing the
    ///         evaluation-time glob. This class's own remarks assert the opposite, and were written on the
    ///         belief that the app case was the dangerous one. It is not.
    ///     </para>
    ///     <para>
    ///         An RCL is different, and the difference is total: its assets come from the <b>evaluated</b>
    ///         <c>@(Content)</c>, so a stylesheet generated later never enters the library's manifest and
    ///         never reaches the consuming app's publish. That shipped — from #914 until the fix this test
    ///         accompanies, every CI deploy of the docs showcase served
    ///         <c>_content/Rask.Example.Shared/css/app.css</c> as a 404 and rendered the site unstyled,
    ///         while a committed file in the same wwwroot served fine.
    ///     </para>
    ///     <para>
    ///         Invisible locally, always: a previous build leaves the CSS on disk, so the glob matches and
    ///         every developer publish — and the two cases above — are correct. Only the first build in a
    ///         clone is wrong, which is every CI run and nobody's machine.
    ///     </para>
    /// </remarks>
    [SkippableFact]
    public async Task A_class_librarys_compiled_stylesheet_reaches_the_consuming_apps_publish()
    {
        Skip.IfNot(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        var temp = Path.Combine(Path.GetTempPath(), "rask-tailwind-rcl", Guid.NewGuid().ToString("N"));
        var libDir = Path.Combine(temp, "RTwLib");
        var appDir = Path.Combine(temp, "RTwApp");

        try
        {
            var fs = new SystemFileSystem();
            fs.CreateDirectory(Path.Combine(libDir, "Styles"));
            fs.CreateDirectory(Path.Combine(libDir, "wwwroot", "css"));
            fs.CreateDirectory(appDir);

            // The control. This one IS on disk when the SDK globs wwwroot, so it reaches publish either
            // way — if it is missing too, the failure is _content plumbing and not this bug.
            fs.WriteAllText(Path.Combine(libDir, "wwwroot", "css", "committed.css"), "/* committed */");

            fs.WriteAllText(Path.Combine(libDir, "Styles", "app.css"), "@import \"tailwindcss\";");
            fs.WriteAllText(
                Path.Combine(libDir, "Marker.cs"),
                "namespace RTwLib; public static class Marker { public const string Classes = \"flex gap-4\"; }");

            fs.WriteAllText(
                Path.Combine(libDir, "RTwLib.csproj"),
                $"""
                 <Project Sdk="Microsoft.NET.Sdk.Razor">
                   <PropertyGroup>
                     <TargetFramework>net10.0</TargetFramework>
                     <StaticWebAssetBasePath>_content/RTwLib</StaticWebAssetBasePath>
                   </PropertyGroup>
                   <ItemGroup>
                     <PackageReference Include="Rask.Server" Version="{version}"/>
                   </ItemGroup>
                 </Project>
                 """);

            fs.WriteAllText(
                Path.Combine(appDir, "RTwApp.csproj"),
                $"""
                 <Project Sdk="Microsoft.NET.Sdk.Web">
                   <PropertyGroup>
                     <TargetFramework>net10.0</TargetFramework>
                     <!-- WebApplication comes from Microsoft.AspNetCore.Builder, which the Web SDK adds
                          only as an IMPLICIT using. Without this the one-line host does not compile and
                          the test fails at the publish, long before it can say anything about CSS. -->
                     <ImplicitUsings>enable</ImplicitUsings>
                     <Nullable>enable</Nullable>
                   </PropertyGroup>
                   <ItemGroup>
                     <PackageReference Include="Rask.Server" Version="{version}"/>
                     <!-- Absolute, and spelled from the same string as every other path here. A
                          relative "..\RTwLib\RTwLib.csproj" made the static-web-assets reference check
                          fail on macOS: MSBuild resolved the library through /private/var and the app
                          through /var (the same directory, since /var is a symlink), and the target
                          compares those as strings — "Unable to find a project reference for project
                          configuration item". -->
                     <ProjectReference Include="{Path.Combine(libDir, "RTwLib.csproj").Replace('\\', '/')}"/>
                   </ItemGroup>
                 </Project>
                 """);

            fs.WriteAllText(
                Path.Combine(appDir, "Program.cs"),
                "var b = WebApplication.CreateBuilder(args); var a = b.Build(); a.Run();");

            CliBuildE2E.WriteNuGetConfig(fs, temp, feed);

            // Never published into before, so a stale file cannot make a broken publish look correct.
            var publishDir = Path.Combine(temp, "published");

            var (exit, output) = await CliBuildE2E.RunDotnet(
                $"publish \"{Path.Combine(appDir, "RTwApp.csproj")}\" -c Release -o \"{publishDir}\" -m:1 -nodeReuse:false");

            Assert.True(exit == 0, $"publish failed.{output}");

            var contentCss = Path.Combine(publishDir, "wwwroot", "_content", "RTwLib", "css");

            Assert.True(
                File.Exists(Path.Combine(contentCss, "committed.css")),
                $"the control file is missing from {contentCss} — this is _content plumbing failing, not "
                + "the generated-asset bug this test is about.");

            var generated = Path.Combine(contentCss, "app.css");
            Assert.True(
                File.Exists(generated),
                $"the class library compiled Tailwind and the consuming app's publish did not carry it: "
                + $"{generated} is absent while committed.css beside it is present. Any page styled by this "
                + "library renders unstyled, and the build succeeds — which is why only the artifact can "
                + "catch it.");

            Assert.True(
                new FileInfo(generated).Length > 0,
                $"{generated} was published but is empty — Tailwind scanned no source.");
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); }
            catch (IOException) { /* best effort */ }
            catch (UnauthorizedAccessException) { /* best effort */ }
        }
    }
}
