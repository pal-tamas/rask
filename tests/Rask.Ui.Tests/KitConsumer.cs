using System.Diagnostics;

namespace Rask.UiTests;

/// <summary>
///     A stand-in for an app that takes the kit in: a directory laid out the way the build leaves one
///     (<c>Styles/app.css</c>, the kit's Tailwind sources in <c>Styles/vendor/</c>), compiled by the same
///     standalone engine, with no <c>node_modules</c> and no <c>package.json</c>.
/// </summary>
internal static class KitConsumer
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(120);

    private static readonly string _kit = Path.Combine(RepoRoot.FullPath, "src", "Rask.Ui");

    /// <summary>What an app's stylesheet says to take the kit in.</summary>
    public const string Import = "@import \"./vendor/rask-ui.css\";";

    /// <summary>Every class the kit's own compiled sheet defines — the list the package hands an app.</summary>
    public static IReadOnlySet<string> Classes { get; } =
        File.ReadAllLines(ClassList()).Where(l => l.Length > 0).ToHashSet(StringComparer.Ordinal);

    /// <summary>The kit's Tailwind sources, by the name each has in an app's <c>Styles/vendor/</c>.</summary>
    public static IReadOnlyDictionary<string, string> Sources() => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["rask-ui.css"] = Path.Combine(_kit, "Styles", "rask-ui.css"),
        ["rask-ui.kit.css"] = Path.Combine(_kit, "Styles", "ui.css"),
        ["rask-ui.classes.txt"] = ClassList(),
        ["daisyui.mjs"] = Path.Combine(_kit, "Styles", "daisyui.mjs"),
    };

    /// <summary>Compiles <paramref name="sheet" /> in an app whose one page writes <paramref name="markup" />.</summary>
    public static string Compile(string sheet, string markup, bool withKit = true)
    {
        var dir = Path.Combine(Path.GetTempPath(), "rask-kit-consumer-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(dir, "Styles", "vendor"));
        Directory.CreateDirectory(Path.Combine(dir, "Features"));

        try
        {
            if (withKit)
            {
                foreach (var (name, source) in Sources())
                {
                    File.Copy(source, Path.Combine(dir, "Styles", "vendor", name));
                }
            }

            File.WriteAllText(Path.Combine(dir, "Styles", "app.css"), sheet);

            // Stands in for a scaffolded page. Tailwind scans the tree it runs in and a C# component's
            // classes are ordinary string literals, so this is found with nothing telling it to.
            File.WriteAllText(
                Path.Combine(dir, "Features", "HomePage.cs"),
                $$"""public static class HomePage { public const string Markup = "{{markup}}"; }""");

            return Run(StandaloneEngine(), dir, "--input", "Styles/app.css", "--output", "out.css").Output("out.css");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { /* left behind on a locked file */ }
        }
    }

    /// <summary>Runs <paramref name="file" /> in <paramref name="dir" /> and hands back how it ended.</summary>
    public static Finished Run(string file, string dir, params string[] arguments)
    {
        var started = Stopwatch.StartNew();
        var info = new ProcessStartInfo(file) { WorkingDirectory = dir, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        // The same switches the gate scripts set: no telemetry spool, no first-run banner in the output.
        info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        info.Environment["DOTNET_NOLOGO"] = "1";

        using var process = Process.Start(info)!;

        // Drained while it runs, not after: a redirected pipe nobody reads fills up, the process blocks
        // writing to it, and the wait below times out on something that was only waiting for its reader.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(_timeout))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* exited meanwhile */ }

            Assert.Fail(
                $"{Path.GetFileName(file)} did not finish within {_timeout.TotalSeconds:0} s (killed after "
                + $"{started.Elapsed.TotalSeconds:0.0} s). That is a slow or starved machine, not a wrong answer.");
        }

        process.WaitForExit(); // flushes the redirected streams
        return new Finished(dir, process.ExitCode, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
    }

    internal sealed record Finished(string Directory, int ExitCode, string Text)
    {
        public string Output(string name)
        {
            var path = Path.Combine(Directory, name);
            Assert.True(File.Exists(path), $"exit code {ExitCode} and no {name}:\n{Text}");
            return File.ReadAllText(path);
        }
    }

    /// <summary>The list the kit's build wrote beside its compiled sheet.</summary>
    /// <remarks>
    ///     Out of <c>obj/</c>, where the package is packed from and an in-repo consumer copies from. The face
    ///     this test project was built for, and no other: building it builds that face of <c>Rask.Ui</c>
    ///     alone, so the browser face's list can be one build behind — and it sorts first.
    /// </remarks>
    private static string ClassList()
    {
        var face = Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        var list = Path.Combine(_kit, "obj", face, "rask-ui.classes.txt");

        Assert.True(
            File.Exists(list),
            $"no {list}: the kit's build writes it from its compiled sheet (RaskTailwindClassList), and "
            + "without it an app that imports the kit compiles none of its classes.");

        return list;
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
            $"no standalone Tailwind engine is cached under {root}, so nothing here can be compiled. "
            + "Building Rask.Ui downloads it.");

        return engine!;
    }
}
