using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Rask.Ui.Tests;

/// <summary>
///     The kit ships daisyUI's plugin bundle, so a consuming app can compile daisyUI itself.
/// </summary>
/// <remarks>
///     <para>
///         The compiled sheet this package links carries the classes <b>its own</b> components write,
///         because Tailwind only emits a class it can see. An app writing <c>card-body</c> in its own
///         markup gets nothing from it — nothing in the kit names that class. So an app that wants to
///         write daisyUI directly has to run the plugin over its own sources.
///     </para>
///     <para>
///         It cannot reach it the usual way. Tailwind resolves <c>@plugin "daisyui"</c> the way Node
///         does, by walking up for a <c>node_modules</c>, and the standalone engine the C# hosts use
///         carries no package tree at all. Shipping the bundle and pointing at it by relative path is
///         what keeps "the SDK is all you need" true for an app that writes daisyUI class names.
///     </para>
/// </remarks>
public sealed class DaisyUiPluginDeliveryTests
{
    private static readonly string _targets = ReadTargets();

    [Fact]
    public void TheBundleIsPackedBesideTheTargets()
    {
        // An EmbeddedResource is only reachable at run time, and this copy has to happen at build
        // time — before Tailwind runs, not after the app has started.
        var csproj = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Rask.Ui", "Rask.Ui.csproj"));

        Assert.Contains("Styles\\vendor\\daisyui.mjs", csproj, StringComparison.Ordinal);
        Assert.Contains("PackagePath=\"build\\\"", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void TheInRepoFallbackNamesNoTargetFramework()
    {
        // The stylesheet's fallback needs two steps because build output is per-TFM, and getting that
        // wrong shipped rask.sh grey (see KitStylesheetResolutionTests). This file has no such
        // problem and must not grow one: the bundle is a SOURCE file no build produces, so it is on
        // disk in a clean checkout whether or not any face of the kit has been compiled.
        var literals = Regex.Matches(_targets, @"obj/net\d+\.\d+[a-z-]*/[^""]*daisyui\.mjs");

        Assert.True(
            literals.Count == 0,
            "the daisyUI bundle is a source file, so naming a TFM in its path can only make it "
            + $"findable on some machines: {string.Join(", ", literals.Select(m => m.Value))}");
    }

    [Fact]
    public void ItIsOptInLikeTheStylesheet()
    {
        // Referencing the kit is not the same as wanting to compile daisyUI yourself. An app drawing
        // with Ui* components alone needs none of this.
        Assert.Contains(
            "<RaskUiWriteDaisyUiPlugin Condition=\"'$(RaskUiWriteDaisyUiPlugin)' == ''\">false</RaskUiWriteDaisyUiPlugin>",
            _targets,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingBundleFailsTheBuild()
    {
        // Same reasoning the stylesheet target records: the project asked for this file by name, so
        // there is no ambiguity left for a warning to be kind about. Without it the app compiles a
        // stylesheet with no daisyUI in it and renders every btn and card as unstyled text, green.
        var target = _targets[_targets.IndexOf("RaskUiWriteDaisyUiPlugin\"", StringComparison.Ordinal)..];

        Assert.Contains("<Error Condition=", target, StringComparison.Ordinal);
        Assert.DoesNotContain("<Warning Condition=", target, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCopyIsOrderedBeforeTheTailwindCompile()
    {
        // Rask.Tailwind also hooks BeforeBuild, and the order between two targets sharing one
        // BeforeTargets is import order — which this file does not get to decide. Naming the compile
        // directly is the only thing that guarantees the bundle is on disk before it is loaded.
        Assert.Contains(
            "BeforeTargets=\"_RaskTailwindBuild;BeforeBuild\"",
            _targets,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AConsumersOwnSheetCompilesDaisyUiFromTheBundle()
    {
        // The claim the whole approach rests on, run for real rather than reasoned about: a sheet
        // shaped like the one `rask new` writes, in a directory with no node_modules and no
        // package.json, loading the bundle by relative path.
        var dir = Path.Combine(Path.GetTempPath(), "rask-daisyui-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(dir, "Styles", "vendor"));
        Directory.CreateDirectory(Path.Combine(dir, "Features"));

        try
        {
            File.Copy(
                Path.Combine(RepoRoot(), "src", "Rask.Ui", "Styles", "vendor", "daisyui.mjs"),
                Path.Combine(dir, "Styles", "vendor", "daisyui.mjs"));

            File.WriteAllText(Path.Combine(dir, "Styles", "app.css"), """
                @layer properties, theme, base, components, daisyui, utilities;

                @import "tailwindcss";

                @source not "./vendor";

                @plugin "./vendor/daisyui.mjs";
                """);

            // Stands in for a scaffolded page. Tailwind scans the tree it runs in and a C# component's
            // classes are ordinary string literals, so this is found with nothing telling it to.
            File.WriteAllText(Path.Combine(dir, "Features", "HomePage.cs"), """
                public static class HomePage
                {
                    public const string Markup =
                        "navbar bg-base-100 shadow-sm hero bg-base-200 py-16 card card-body "
                        + "card-actions btn btn-primary alert alert-error footer px-8";
                }
                """);

            var css = Compile(dir);

            foreach (var name in (string[])["card", "card-body", "card-actions", "btn", "navbar", "hero", "footer", "alert"])
            {
                Assert.True(
                    Regex.IsMatch(css, $@"^\s*\.{Regex.Escape(name)}\s*\{{", RegexOptions.Multiline),
                    $".{name} is not in the compiled sheet, so an app writing it renders unstyled.");
            }

            // The app's own utilities compile in the same pass — this is one stylesheet, not two.
            Assert.True(Regex.IsMatch(css, @"^\s*\.px-8\s*\{", RegexOptions.Multiline));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { /* left behind on a locked file */ }
        }
    }

    [Fact]
    public void TheBundleIsNotScannedAsASafelist()
    {
        // 348 KB of daisyUI's own code, naming every class daisyUI defines. Scanned, it acts as a
        // safelist for the whole library and the sheet carries every component whether or not the app
        // uses one — which reads as correct, because a sheet containing too much looks exactly like a
        // sheet containing enough. `@source not "./vendor"` is what stops it, and a scaffold that
        // forgets the line gets a much larger sheet and no error.
        var dir = Path.Combine(Path.GetTempPath(), "rask-daisyui-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(dir, "Styles", "vendor"));

        try
        {
            File.Copy(
                Path.Combine(RepoRoot(), "src", "Rask.Ui", "Styles", "vendor", "daisyui.mjs"),
                Path.Combine(dir, "Styles", "vendor", "daisyui.mjs"));

            File.WriteAllText(Path.Combine(dir, "Styles", "app.css"), """
                @import "tailwindcss";

                @source not "./vendor";

                @plugin "./vendor/daisyui.mjs";
                """);

            var css = Compile(dir);

            // Nothing here uses any of these, so none may be emitted.
            foreach (var unused in (string[])["timeline", "carousel", "kbd", "steps", "rating"])
            {
                Assert.False(
                    Regex.IsMatch(css, $@"^\s*\.{unused}\s*\{{", RegexOptions.Multiline),
                    $".{unused} is in a sheet whose source never names it — the bundle is being "
                    + "scanned as a safelist, so this sheet carries the whole library.");
            }
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { /* left behind on a locked file */ }
        }
    }

    private static string Compile(string dir)
    {
        var output = Path.Combine(dir, "out.css");
        var engine = StandaloneEngine();

        using var process = Process.Start(new ProcessStartInfo(engine)
        {
            ArgumentList = { "--input", "Styles/app.css", "--output", "out.css" },
            WorkingDirectory = dir,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        })!;

        process.WaitForExit(milliseconds: 120_000);

        Assert.True(
            File.Exists(output),
            $"the standalone engine wrote no stylesheet: {process.StandardError.ReadToEnd()}");

        return File.ReadAllText(output);
    }

    /// <summary>The engine Rask.Tailwind cached, per user, when it built this repository's own sheets.</summary>
    /// <remarks>
    ///     Not downloaded here. Running this test project builds <c>Rask.Ui</c>, which compiles its own
    ///     stylesheet, so the engine is on disk by the time any of this runs. If it is not, the cause is
    ///     worth seeing rather than skipping past.
    /// </remarks>
    private static string StandaloneEngine()
    {
        var root = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "rask", "tailwind")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".rask", "tailwind");

        var engine = Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "tailwindcss-*", SearchOption.AllDirectories).FirstOrDefault()
            : null;

        Assert.True(
            engine is not null,
            $"no standalone Tailwind engine is cached under {root}, so the claim this file exists to "
            + "prove cannot be tested. Building Rask.Ui downloads it.");

        return engine!;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadTargets()
    {
        var path = Path.Combine(RepoRoot(), "src", "Rask.Ui", "build", "Rask.Ui.targets");
        Assert.True(File.Exists(path), $"the kit's targets moved: {path}");

        // Comments stripped: this file documents the hazards it guards against, and a test that reads
        // prose fails on the explanation of the thing it is checking for.
        return Regex.Replace(File.ReadAllText(path), "<!--.*?-->", string.Empty, RegexOptions.Singleline);
    }
}
