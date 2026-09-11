using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;
using Rask.Cli.Templates;

namespace Rask.Templates.E2E.Tests;

/// <summary>
///     Every template `rask new` offers is scaffolded and built against THIS commit's Rask packages.
/// </summary>
/// <remarks>
///     <para>
///         The claim under test is the plain one the unit suites cannot make: that what the scaffolder
///         writes compiles. Eleven of the fifteen templates had nothing making it — the six meta ones
///         were never built by anything, and Preact, Vue, Solid, Svelte and Lit had no build gate at all.
///     </para>
///     <para>
///         Built through the same dispatch <see cref="NewCommand"/> uses, so the arm under test is the
///         one that ships, and with the same batteries <c>ToBatteries</c> resolves, so a template is
///         built in the shape a user actually gets rather than a hand-picked one.
///     </para>
/// </remarks>
public sealed class TemplateBuildE2ETests
{
    internal const string SkipReason =
        "Template gate: set RASK_TEMPLATE_E2E=1 to run it (it packs this commit's Rask packages, "
        + "restores and builds every scaffolded template, so it needs the SDK and a network). "
        + "See scripts/run-template-e2e.sh.";

    internal static bool Enabled => Environment.GetEnvironmentVariable("RASK_TEMPLATE_E2E") == "1";

    public static TheoryData<string> Templates() => [.. TemplateCatalog.Keys];

    [SkippableTheory]
    [MemberData(nameof(Templates))]
    public async Task Every_template_compiles(string key)
    {
        Skip.IfNot(Enabled, SkipReason);

        var (feed, version) = await CliBuildE2E.LocalFeed.Value;
        var name = "Tmpl" + key.Replace("-", "", StringComparison.Ordinal);
        var work = NewWorkingDirectory();

        try
        {
            var projectDirectory = Path.Combine(work, name);
            var result = Scaffold(key, projectDirectory, name, version, islands: []);
            Write(result, projectDirectory, feed);

            // The FRONT END is deliberately off here: this asserts the C# half compiles, and the npm
            // half is four to six minutes per template on a cold cache. Every_front_end_template_builds
            // is where that is paid, behind its own switch.
            var (exit, output) = await CliBuildE2E.RunDotnet(
                $"build \"{Path.Combine(projectDirectory, name + ".csproj")}\" -warnaserror -m:1 "
                + "-p:RaskSpaBuild=false -p:RaskMetaBuild=false -p:RaskExternalBuild=false");

            Assert.True(exit == 0, $"--template {key} does not compile:\n{CliBuildE2E.Diagnostics(output)}");
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(work);
        }
    }

    [SkippableTheory]
    [InlineData("server", "react")]
    [InlineData("server", "blazor")]
    [InlineData("wasm", "lit")]
    public async Task An_islands_host_compiles(string template, string runtime)
    {
        Skip.IfNot(Enabled, SkipReason);

        var (feed, version) = await CliBuildE2E.LocalFeed.Value;
        var name = $"Isl{template}{runtime}";
        var work = NewWorkingDirectory();

        try
        {
            var projectDirectory = Path.Combine(work, name);
            var result = Scaffold(template, projectDirectory, name, version, [runtime]);
            Write(result, projectDirectory, feed);

            var (exit, output) = await CliBuildE2E.RunDotnet(
                $"build \"{Path.Combine(projectDirectory, name + ".csproj")}\" -warnaserror -m:1 "
                + "-p:RaskSpaBuild=false -p:RaskMetaBuild=false -p:RaskExternalBuild=false");

            Assert.True(
                exit == 0,
                $"--template {template} --islands {runtime} does not compile:\n"
                + CliBuildE2E.Diagnostics(output));
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(work);
        }
    }

    /// <summary>
    ///     The same dispatch <see cref="NewCommand"/> performs, so a key wired to the wrong generator
    ///     fails here rather than producing a different template's project.
    /// </summary>
    internal static ScaffoldResult Scaffold(
        string key, string projectDirectory, string name, string version, IReadOnlyList<string> islands)
    {
        Assert.True(TemplateCatalog.TryGet(key, out var template));

        var wasm = template.SupportedFlags.Contains("wasm");
        var batteries = NewCommand.ToBatteries(template, [], wasm);

        if (SpaFramework.TryGet(key, out var spa))
        {
            return ProjectGenerator.GenerateSpa(projectDirectory, name, spa, batteries, version);
        }

        if (MetaTemplate.TryGet(key, out var meta))
        {
            return ProjectGenerator.GenerateMeta(projectDirectory, name, meta, batteries, version);
        }

        return key switch
        {
            "wasm" => ProjectGenerator.GenerateWasm(
                projectDirectory, name, batteries.Pwa, batteries.Docker, version, batteries, islands),
            _ => ProjectGenerator.GenerateServer(projectDirectory, name, batteries, version, islands),
        };
    }

    /// <summary>
    ///     Writes a scaffold to disk and points it at the local feed.
    /// </summary>
    /// <remarks>
    ///     Binary files are written as BYTES. The templates carry a PNG and two .ico favicons, and
    ///     writing those through WriteAllText re-encodes them as UTF-8 — a corruption that a build does
    ///     not notice and a browser does.
    /// </remarks>
    internal static void Write(ScaffoldResult result, string projectDirectory, string feed)
    {
        var fs = new SystemFileSystem();

        foreach (var file in result.Files)
        {
            fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
            if (file.Bytes is { } bytes)
            {
                fs.WriteAllBytes(file.Path, bytes);
            }
            else
            {
                fs.WriteAllText(file.Path, file.Content);
            }
        }

        CliBuildE2E.WriteNuGetConfig(fs, projectDirectory, feed);
    }

    /// <summary>
    ///     A scratch directory, on a path with no symbolic link in it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         On macOS <c>Path.GetTempPath()</c> answers <c>/var/folders/…</c>, and <c>/var</c> is a
    ///         symlink to <c>/private/var</c>. MSBuild resolves some paths through it and leaves others
    ///         alone, so a project restored as <c>/private/var/…/App.Components.csproj</c> and a
    ///         reference recorded as <c>/var/…/App.Components.csproj</c> are the same file and two
    ///         different strings.
    ///     </para>
    ///     <para>
    ///         The SDK's static-web-assets check compares them textually and reports "Unable to find a
    ///         project reference for project configuration item", which reads exactly like a broken
    ///         template. It cost an hour here: the same scaffold built by hand under a non-symlinked
    ///         path succeeded every time, which is what finally separated the environment from the code.
    ///         Only a project that references ANOTHER project is affected, so this surfaced the day the
    ///         Blazor island grew a Razor Class Library and not before.
    ///     </para>
    /// </remarks>
    internal static string NewWorkingDirectory()
    {
        var temp = Path.GetTempPath();
        var canonical = Path.Combine("/private", temp.TrimStart('/'));
        if (OperatingSystem.IsMacOS() && temp.StartsWith("/var/", StringComparison.Ordinal)
            && Directory.Exists(canonical))
        {
            temp = canonical;
        }

        var path = Path.Combine(temp, "rask-template-e2e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
