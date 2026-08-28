using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     <c>--tailwind</c> on a front-end template, proven by the real scaffolder and the real bundler.
/// </summary>
/// <remarks>
///     <para>
///         Everything else about these templates is asserted against strings the generator produced. This
///         one cannot be: what makes Tailwind work is a chain that runs entirely inside somebody else's
///         toolchain — the adapter has to be one the bundler loads, the stylesheet Rask overlays has to be
///         the file that bundler's entry point actually pulls in, and the utility has to survive into the
///         emitted CSS. Every link fails **silently**: the build succeeds and the page is unstyled
///         (<see href="https://github.com/pal-tamas/rask/issues/839" />).
///     </para>
///     <para>
///         That is not hypothetical. Angular shipped with <c>@tailwindcss/vite</c> and no Vite config to
///         register it in — its build config belongs to <c>@angular/build</c> — so every utility class was
///         missing from the output with nothing reporting it. It was found by reading the generator, not by
///         any test, because no test ran a bundler.
///     </para>
///     <para>
///         <b>Two frameworks, chosen for their adapters rather than their popularity.</b> React stands for
///         the five templates that own a <c>vite.config.ts</c> and take <c>@tailwindcss/vite</c>; Angular is
///         the only one that does not, and takes <c>@tailwindcss/postcss</c> through a <c>.postcssrc.json</c>
///         instead. Those two paths are the whole of the branch that got Angular wrong. Running all seven
///         would add npm installs without adding a code path.
///     </para>
///     <para>
///         <b>The scaffolders decide whether Node is adequate; this only propagates what they decided.</b>
///         Both run through <c>npx</c> and both state their own requirements — Angular's CLI in as many
///         words, and its floor is above Vite's. A refusal is therefore a failure here, not a skip. It
///         used to be a skip, which meant that on a machine whose <c>PATH</c> resolved an older Node than
///         the one installed, this suite reported green while the Angular case — the one covering the
///         adapter path that shipped broken — never ran at all.
///     </para>
///     <para>
///         Gated with the other build E2Es, and slow: a real <c>npm install</c> per case. Needs the network.
///     </para>
/// </remarks>
public sealed class SpaTailwindBuildE2ETests
{
    /// <summary>A class no scaffolded file contains, injected into the page under test.</summary>
    /// <remarks>
    ///     The templates' own markup carries no classes at all, so there is nothing native to look for. An
    ///     injected one is better anyway: it proves the scan reaches the file the bundler compiles, and a
    ///     deliberately odd value cannot be in Tailwind's output by coincidence.
    /// </remarks>
    private const string Probe = "mt-[13px]";

    /// <summary>What the probe compiles to. Asserted rather than the class name alone.</summary>
    /// <remarks>
    ///     The class name appears in the CSS as part of a selector only if Tailwind generated a rule for it.
    ///     Checking the declaration too is what separates "Tailwind emitted this utility" from "the source
    ///     file happened to be inlined somewhere in the bundle".
    /// </remarks>
    private const string ProbeDeclaration = "margin-top:13px";

    [SkippableTheory]
    [InlineData("react")]
    [InlineData("angular")]
    public async Task A_utility_class_reaches_the_bundlers_emitted_css(string frameworkKey)
    {
        Skip.IfNot(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var (feed, version) = await CliBuildE2E.LocalFeed.Value;
        Assert.True(SpaFramework.TryGet(frameworkKey, out var framework));

        var name = "Tw" + frameworkKey;
        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            var result = ProjectGenerator.GenerateSpa(
                projectDir, name, framework, new ServerBatteries(), version);

            // The same order `rask new` uses: the scaffolder first, our overlay on top, patches last.
            // Any other order tests a project nobody can produce.
            Directory.CreateDirectory(projectDir);
            foreach (var external in result.ExternalScaffolds)
            {
                var (exit, output) = await CliBuildE2E.RunProcess(external.Command, external.Arguments, projectDir);

                // Whatever the scaffolder says goes, including "your Node is too old" — Angular's CLI
                // carries its own floor, above Vite's, and refuses outright below it. That verdict is
                // propagated as a FAILURE rather than caught and turned into a skip: a skip reads as
                // green, and the case being skipped is the one covering the adapter path that already
                // shipped broken once. Diagnostics folds the CLI's own sentence into the message, so
                // whoever hits this is told what to install rather than just handed an exit code.
                Assert.True(exit == 0, $"[{frameworkKey}] {external.Command} failed.{CliBuildE2E.Diagnostics(output)}");
            }

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            foreach (var patch in result.Patches)
            {
                if (File.Exists(patch.Path))
                {
                    fs.WriteAllText(patch.Path, patch.Transform(await File.ReadAllTextAsync(patch.Path)));
                }
            }

            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            var client = Path.Combine(projectDir, name + ".Client");
            InjectProbe(client, frameworkKey);

            // ONE command, and deliberately the .NET one. Rask.Spa.Hosting's targets own the whole chain
            // from here — probe node, install the client's dependencies, emit the TypeScript contracts
            // from the server's message records, then run the bundler. Driving npm directly would skip the
            // emit and test a client that cannot type-check, which is exactly what it did on the first run
            // of this test: `Property 'visits' does not exist on type '{}'`.
            var csproj = Path.Combine(projectDir, name + ".Server", name + ".Server.csproj");
            var (buildExit, buildOutput) = await CliBuildE2E.RunDotnet($"build \"{csproj}\" -m:1");
            Assert.True(buildExit == 0, $"[{frameworkKey}] the solution failed to build.{CliBuildE2E.Diagnostics(buildOutput)}");

            var dist = Path.Combine(client, framework.DistFor(name).Replace('/', Path.DirectorySeparatorChar));
            Assert.True(Directory.Exists(dist), $"[{frameworkKey}] the bundler wrote no {dist}.");

            var css = Directory.EnumerateFiles(dist, "*.css", SearchOption.AllDirectories).ToArray();
            Assert.True(css.Length > 0, $"[{frameworkKey}] the build emitted no CSS at all under {dist}.");

            var emitted = string.Concat(await Task.WhenAll(css.Select(path => File.ReadAllTextAsync(path))));

            // The seam. A missing adapter, an overlay written to a filename nothing imports, or a scan
            // rooted at the wrong directory all land here — and nowhere else, because each of them leaves
            // a green build behind.
            Assert.True(
                emitted.Contains(ProbeDeclaration, StringComparison.Ordinal),
                $"[{frameworkKey}] '{Probe}' never reached the emitted CSS, so Tailwind is wired but not "
                + "working: the adapter is not loaded, the stylesheet is not the one the entry point "
                + $"imports, or the scan is rooted elsewhere. Emitted {emitted.Length} bytes of CSS.");

            // Preflight, so the assertion above cannot be satisfied by an unprocessed @import passing
            // through as literal text.
            Assert.Contains("box-sizing", emitted, StringComparison.Ordinal);
            Assert.DoesNotContain("@import \"tailwindcss\"", emitted, StringComparison.Ordinal);

            // Everything above is satisfied by the PROBE, which this test injects itself — so it proves
            // the compiler runs on what it is handed, and nothing about the page the user opens. That gap
            // shipped: the starter's markup carries no classes, `--tailwind` replaced the scaffolder's
            // stylesheet with a bare @import, and preflight then reset the tags that stylesheet had been
            // styling. The flag produced a WORSE-looking page than no flag at all, with every check green
            // (https://github.com/pal-tamas/rask/issues/859).
            //
            // The starter now styles its own elements from the base layer, and these are what say so.
            // Layout on `main` and a radius on `button`: preflight sets neither, so a rule carrying them
            // is the starter's own and not something Tailwind would have emitted regardless.
            AssertStarterRule(emitted, "main", "display:flex", frameworkKey);
            AssertStarterRule(emitted, "button", "border-radius", frameworkKey);
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(temp);
        }
    }

    /// <summary>Puts <see cref="Probe" /> on an element the framework actually renders.</summary>
    /// <remarks>
    ///     Appended to the entry stylesheet's own component rather than guessing at markup: every template
    ///     has a different component file and a different templating syntax, and Tailwind v4 scans plain
    ///     text — it does not need the class to be in valid JSX to find it, only in a file under the tree it
    ///     scans, next to the code the bundler compiles.
    /// </remarks>
    private static void InjectProbe(string client, string frameworkKey)
    {
        var file = frameworkKey == "angular"
            ? Path.Combine(client, "src", "app", "app.ts")
            : Path.Combine(client, "src", "main.tsx");

        Assert.True(File.Exists(file), $"[{frameworkKey}] expected an entry component at {file}.");

        File.AppendAllText(file, $"\n// tailwind probe: {Probe}\n");
    }

    /// <summary>Where a selector can begin: right after the previous rule's brace, or at the start.</summary>
    private static readonly char[] SelectorStops = ['{', '}'];

    /// <summary>
    ///     Asserts the emitted CSS carries a rule whose selector is the BARE element and whose body holds
    ///     <paramref name="declaration" />.
    /// </summary>
    /// <remarks>
    ///     Rule by rule rather than by substring, because Tailwind's preflight emits element selectors of
    ///     its own — <c>emitted.Contains("button")</c> is true even with the starter's whole base layer
    ///     dropped, which is precisely the state this is here to catch. Pairing the selector with a
    ///     declaration preflight does not set is what makes the check mean something.
    /// </remarks>
    private static void AssertStarterRule(string css, string element, string declaration, string frameworkKey)
    {
        for (var open = css.IndexOf('{', StringComparison.Ordinal); open >= 0;
             open = css.IndexOf('{', open + 1))
        {
            var close = css.IndexOf('}', open + 1);
            var nested = css.IndexOf('{', open + 1);

            // A wrapper — @layer, @media, @supports. Its inner rules are reached on their own pass, so
            // stepping over it here would skip exactly the rules being looked for: Tailwind v4 emits the
            // base layer as a real `@layer base { … }`.
            if (close < 0 || (nested >= 0 && nested < close))
            {
                continue;
            }

            var boundary = open == 0 ? -1 : css.LastIndexOfAny(SelectorStops, open - 1);
            var selector = css[(boundary + 1)..open];
            var body = css[(open + 1)..close].Replace(" ", string.Empty, StringComparison.Ordinal);

            if (body.Contains(declaration, StringComparison.Ordinal)
                && selector.Split(',').Any(part => part.Trim() == element))
            {
                return;
            }
        }

        Assert.Fail(
            $"[{frameworkKey}] the emitted CSS styles no bare '{element}' with '{declaration}', so the "
            + "starter's own base layer never reached the output. Tailwind compiles (the probe above "
            + "proved that), but the page the template ships renders unstyled — which is worse than the "
            + $"same project without --tailwind. Emitted {css.Length} bytes of CSS.");
    }
}
