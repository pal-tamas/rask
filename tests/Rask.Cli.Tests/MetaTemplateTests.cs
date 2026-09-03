using System.Text.RegularExpressions;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     <c>rask new --template nuxt</c> and the five beside it: an ASP.NET host that supervises the
///     framework's own Node server, with the front end scaffolded by that framework's own creator.
/// </summary>
public sealed class MetaTemplateTests
{
    private static readonly string _repoRoot = LocateRepoRoot();

    private static ScaffoldResult Generate(string key)
    {
        Assert.True(MetaTemplate.TryGet(key, out var framework), $"{key} is not a meta template.");
        return ProjectGenerator.GenerateMeta("/out", "Shop", framework, new ServerBatteries(), "1.0.0");
    }

    private static string File(ScaffoldResult result, string suffix) =>
        result.Files.Single(f => f.Path.Replace('\\', '/').EndsWith(suffix, StringComparison.Ordinal)).Content;

    [Fact]
    public void Every_template_is_a_framework_the_build_knows()
    {
        // The template key IS the <RaskMetaFramework> value, and the build's own table is what decides
        // whether that value does anything. A template named for a framework the targets have never
        // heard of scaffolds an app whose front end is never installed, never built and never published
        // — with no error anywhere, because the property simply matches no branch.
        var targets = System.IO.File.ReadAllText(System.IO.Path.Combine(
            _repoRoot, "src", "Rask.Meta.Hosting", "build", "Rask.Meta.Hosting.targets"));

        var known = Regex.Matches(targets, @"'\$\(RaskMetaFramework\)' == '(?<name>[a-z-]+)'")
            .Select(m => m.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var template in MetaTemplate.All)
        {
            Assert.True(
                known.Contains(template.Key),
                $"'{template.Key}' is offered by `rask new` but is not in the build's framework table.");
        }

        Assert.Equal(6, MetaTemplate.All.Count);
    }

    [Fact]
    public void Generated_code_lands_where_that_framework_keeps_source()
    {
        // app/ for Nuxt and Next's App Router, src/ for the rest — and the build decides this
        // independently, in _RaskMetaSourceDir. If the two disagree the scaffold gitignores one
        // directory while the build writes another, so the generated contracts get committed and the
        // imports resolve to nothing.
        var targets = System.IO.File.ReadAllText(System.IO.Path.Combine(
            _repoRoot, "src", "Rask.Meta.Hosting", "build", "Rask.Meta.Hosting.targets"));

        foreach (var template in MetaTemplate.All)
        {
            var start = targets.IndexOf($"'$(RaskMetaFramework)' == '{template.Key}'", StringComparison.Ordinal);
            Assert.True(start >= 0);

            var end = targets.IndexOf("</When>", start, StringComparison.Ordinal);
            var block = end < 0 ? targets[start..] : targets[start..end];
            var source = Regex.Match(block, @"<_RaskMetaSourceDir>(?<dir>[a-z]+)</_RaskMetaSourceDir>");

            Assert.True(source.Success, $"{template.Key} has no source directory in the targets.");
            Assert.Equal($"{source.Groups["dir"].Value}/rask", template.GeneratedDir);
        }
    }

    [Fact]
    public void The_csproj_names_the_framework_and_nothing_else_has_to()
    {
        var csproj = File(Generate("nuxt"), "Shop.csproj");

        Assert.Contains("<RaskMetaFramework>nuxt</RaskMetaFramework>", csproj, StringComparison.Ordinal);
        Assert.Contains(@"<PackageReference Include=""Rask.Meta.Hosting""", csproj, StringComparison.Ordinal);
        Assert.Contains(@"<PackageReference Include=""Rask.Cqrs.Server""", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void The_api_is_mapped_before_the_front_end_swallows_the_route()
    {
        var program = File(Generate("sveltekit"), "Program.cs");

        var cqrs = program.IndexOf("app.MapRaskCqrs()", StringComparison.Ordinal);
        var meta = program.IndexOf("app.UseRaskMeta()", StringComparison.Ordinal);

        Assert.True(cqrs >= 0 && meta >= 0);

        // UseRaskMeta registers a FALLBACK. An endpoint mapped after it is not unreachable in a way
        // anything reports — it is answered with a rendered page, which reads as a front-end bug.
        Assert.True(cqrs < meta, "MapRaskCqrs must come before UseRaskMeta.");
    }

    [Theory]
    [InlineData("nuxt")]
    [InlineData("nextjs")]
    [InlineData("sveltekit")]
    [InlineData("solidstart")]
    [InlineData("tanstack-start")]
    [InlineData("analog")]
    public void Every_creator_is_told_where_to_put_the_app(string key)
    {
        Assert.True(MetaTemplate.TryGet(key, out var framework));

        var args = framework.Scaffolder("Shop");

        // One rule for all six: a bare, lowercase target, run from inside the project directory. Handing
        // any of them `Shop/client` is what three of them refuse — create-next-app and @tanstack/cli
        // exit on the capital in `Shop`, and create-analog stops to ask, which is a hang.
        Assert.Contains(framework.AppDir, args);
        Assert.DoesNotContain(args, arg => arg.Contains('/', StringComparison.Ordinal) && arg.Contains("lient", StringComparison.Ordinal));

        var result = ProjectGenerator.GenerateMeta(
            "/out", "Shop", framework, new ServerBatteries(), "1.0.0");

        Assert.Equal("Shop", result.ExternalScaffolds.Single().WorkingSubdirectory);

        // create-start-app prints a deprecation notice on every run and points at @tanstack/cli.
        Assert.DoesNotContain(args, arg => arg.Contains("create-start-app", StringComparison.Ordinal));
    }

    [Fact]
    public void The_creators_that_have_to_be_told_not_to_ask_are_told()
    {
        // Each of these was established by running the creator: without the flag it sits on a prompt,
        // and a prompt inside `rask new` is a hang rather than a failure anyone can act on.
        Assert.Contains("--no-gitInit", MetaTemplate.Nuxt.Scaffolder("Shop"));
        Assert.Contains("--template", MetaTemplate.Nuxt.Scaffolder("Shop"));
        Assert.Contains("--non-interactive", MetaTemplate.TanStackStart.Scaffolder("Shop"));
        Assert.Contains("--v2", MetaTemplate.SolidStart.Scaffolder("Shop"));

        // Undocumented, and the only way through: create-analog answers even --help with its first
        // prompt, and understands neither --yes nor --no-tailwind.
        Assert.Contains("--skipTailwind", MetaTemplate.Analog.Scaffolder("Shop"));
    }

    [Fact]
    public void The_node_server_is_arranged_exactly_once_per_framework()
    {
        // Either Rask writes the config that names the node preset, or the creator was asked for it.
        // A framework with neither builds something this host cannot start — and only at startup.
        foreach (var template in MetaTemplate.All)
        {
            var overlaid = template.ConfigFiles.Any(f =>
                f.Content.Contains("node-server", StringComparison.Ordinal)
                || f.Content.Contains("standalone", StringComparison.Ordinal));

            var asked = template.Scaffolder("Shop").Any(arg =>
                arg is "nitro" or "sveltekit-adapter=adapter:node")
                || template.Key is "solidstart" or "analog";

            Assert.True(overlaid || asked, $"{template.Key} arranges no node server.");
        }
    }

    [Fact]
    public void Every_template_arranges_the_alias_exactly_once_and_by_a_mechanism_that_framework_honours()
    {
        // `@rask/*` is what the docs, the generated README and the next-steps text all tell the
        // developer to write, and it has three possible homes — none of which works everywhere:
        //
        //   tsconfig paths   Next, TanStack, SolidStart, Analog
        //   kit.alias        SvelteKit, which GENERATES the tsconfig and rejects a hand-written paths
        //   nuxt.config      Nuxt, which also generates its tsconfig
        //
        // A template with none of them scaffolds an app whose every Rask import fails to resolve, and
        // nothing in this repository would notice: the files are all well-formed. A template with two
        // is worse than one, because the framework-generated half wins and the other is dead text.
        foreach (var template in MetaTemplate.All)
        {
            var viaTsConfig = template.TsConfigFile is { Length: > 0 };
            var viaKit = template.KitAlias;
            var viaOwnConfig = template.ConfigFiles.Any(f =>
                f.Content.Contains("'@rask'", StringComparison.Ordinal));

            var mechanisms = (viaTsConfig ? 1 : 0) + (viaKit ? 1 : 0) + (viaOwnConfig ? 1 : 0);

            Assert.True(
                mechanisms == 1,
                $"{template.Key} declares {mechanisms} ways to map @rask/*, and exactly one works.");
        }
    }

    [Fact]
    public void Every_template_gets_Tailwind_one_way_or_the_other()
    {
        // Styling is not a decision Rask leaves to the template: `rask new --template react` ships
        // Tailwind, and a meta template that quietly did not would differ from it in a way nothing
        // announces. Four creators can be asked; two cannot, and Rask installs it for those.
        foreach (var template in MetaTemplate.All)
        {
            var args = template.Scaffolder("Shop");

            var askedFor = args.Any(arg =>
                arg is "--tailwind" or "with-tailwindcss"
                || arg.Contains("tailwindcss=", StringComparison.Ordinal))
                || template.Key == "tanstack-start";

            var raskAdds = template.TailwindStylesheet is { Length: > 0 };

            Assert.True(askedFor || raskAdds, $"{template.Key} would ship without Tailwind.");
            Assert.False(askedFor && raskAdds, $"{template.Key} would install Tailwind twice.");
        }
    }

    [Fact]
    public void A_standard_TanStack_scaffold_is_what_carries_its_Tailwind()
    {
        // --blank is what turns it off ("standard scaffolds always enable Tailwind"), so its absence is
        // load-bearing rather than an omission — and nothing else in the invocation says so.
        Assert.DoesNotContain("--blank", MetaTemplate.TanStackStart.Scaffolder("Shop"));
    }

    [Fact]
    public void The_two_Rask_installs_pick_one_adapter_each()
    {
        // Two adapters for one compiler. Installing the one nothing reads is silent: the build succeeds
        // with no utilities in the output, which reads as a Tailwind problem and is not.
        var nuxt = ProjectGenerator.AddMetaTailwind("""{"dependencies":{}}""", MetaTemplate.Nuxt);
        var analog = ProjectGenerator.AddMetaTailwind("""{"dependencies":{}}""", MetaTemplate.Analog);

        Assert.Contains("@tailwindcss/vite", nuxt, StringComparison.Ordinal);
        Assert.DoesNotContain("@tailwindcss/postcss", nuxt, StringComparison.Ordinal);

        Assert.Contains("@tailwindcss/postcss", analog, StringComparison.Ordinal);
        Assert.DoesNotContain("@tailwindcss/vite", analog, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nuxt")]
    [InlineData("nextjs")]
    [InlineData("sveltekit")]
    [InlineData("solidstart")]
    [InlineData("tanstack-start")]
    [InlineData("analog")]
    public void The_front_end_folder_is_lowercase_and_the_csproj_says_so(string key)
    {
        // Half of these creators derive an npm package name from the target directory and reject a
        // capital letter in it outright. The whole lane therefore uses `client`, where the SPA lane uses
        // `Client` — and the csproj has to carry RaskMetaAppDir, because the property defaults to the
        // capitalised name. Without it the build looks in one folder while the app is in another, which
        // on a case-sensitive filesystem is simply a front end that was never built.
        Assert.True(MetaTemplate.TryGet(key, out var framework));
        Assert.Equal("client", framework.AppDir);

        var csproj = File(Generate(key), "Shop.csproj");

        Assert.Contains("<RaskMetaAppDir>client</RaskMetaAppDir>", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void The_container_carries_node_because_this_lane_needs_it_at_runtime()
    {
        Assert.True(MetaTemplate.TryGet("nuxt", out var framework));
        var result = ProjectGenerator.GenerateMeta(
            "/out", "Shop", framework, new ServerBatteries { Docker = true }, "1.0.0");

        var dockerfile = File(result, "Dockerfile");

        // The distinguishing cost of this lane, and the thing the SPA lane's Dockerfile deliberately
        // does NOT do: the final stage keeps a node runtime, because the front end has a server of its
        // own that this host supervises for the life of the container.
        var final = dockerfile[dockerfile.IndexOf("AS final", StringComparison.Ordinal)..];
        Assert.Contains("nodejs", final, StringComparison.Ordinal);
    }

    private static string LocateRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir, "Rask.slnx")))
        {
            dir = System.IO.Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return dir!;
    }
}
