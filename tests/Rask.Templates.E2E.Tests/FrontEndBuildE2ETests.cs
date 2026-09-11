using Rask.Cli.Scaffolding;

namespace Rask.Templates.E2E.Tests;

/// <summary>
///     Each front-end template's client installs, lints and builds for real.
/// </summary>
/// <remarks>
///     <para>
///         Behind its own switch because it is the expensive one: a cold client is an <c>npm ci</c> plus
///         a production framework build, measured at four to six minutes per template, and there are
///         thirteen. <see cref="TemplateBuildE2ETests"/> covers the C# half of all fifteen far more
///         cheaply, which is the part that regressed silently before.
///     </para>
///     <para>
///         The lint run is here rather than in the unit gate for the reason that matters: a plugin's
///         exported config name differs per plugin and per major, and a wrong one throws at ESLint
///         STARTUP. Nothing that reads the config file can see that — only running it can.
///     </para>
/// </remarks>
public sealed class FrontEndBuildE2ETests
{
    private const string SkipReason =
        "Front-end template gate: set RASK_TEMPLATE_FRONTEND_E2E=1 (with RASK_TEMPLATE_E2E=1) to run "
        + "it. Each case is a real npm ci plus a production framework build — 4.5-6 minutes per "
        + "template on a cold cache. See scripts/run-template-e2e.sh.";

    private static bool Enabled =>
        TemplateBuildE2ETests.Enabled
        && Environment.GetEnvironmentVariable("RASK_TEMPLATE_FRONTEND_E2E") == "1";

    public static TheoryData<string> FrontEnds() =>
        [.. SpaFramework.All.Select(f => f.Key).Concat(MetaTemplate.All.Select(f => f.Key))];

    [SkippableTheory]
    [MemberData(nameof(FrontEnds))]
    public async Task Every_front_end_template_installs_lints_and_builds(string key)
    {
        Skip.IfNot(Enabled, SkipReason);

        var (feed, version) = await CliBuildE2E.LocalFeed.Value;
        var name = "Fe" + key.Replace("-", "", StringComparison.Ordinal);
        var work = TemplateBuildE2ETests.NewWorkingDirectory();

        try
        {
            var projectDirectory = Path.Combine(work, name);
            var result = TemplateBuildE2ETests.Scaffold(key, projectDirectory, name, version, islands: []);
            TemplateBuildE2ETests.Write(result, projectDirectory, feed);

            var client = Path.Combine(projectDirectory, "client");

            // `npm ci` rather than `npm install`: the lockfile is committed, and the point of committing
            // it is that the tree a user gets is the tree that was tested. If ci cannot use it, the
            // lockfile and the manifest have drifted and that IS the failure.
            var (install, installOutput) = await Npm("ci --no-audit --no-fund", client);
            Assert.True(install == 0, $"{key}: npm ci failed\n{Tail(installOutput)}");

            var (lint, lintOutput) = await Npm("run lint", client);
            Assert.True(lint == 0, $"{key}: npm run lint failed\n{Tail(lintOutput)}");

            var (format, formatOutput) = await Npm("run format:check", client);
            Assert.True(format == 0, $"{key}: npm run format:check failed\n{Tail(formatOutput)}");

            // The whole build, host included, with the front end ON — which is what makes this the only
            // thing that proves the generated TypeScript contracts compile against the client.
            var (build, buildOutput) = await CliBuildE2E.RunDotnet(
                $"build \"{Path.Combine(projectDirectory, name + ".csproj")}\" -warnaserror -m:1");

            Assert.True(
                build == 0,
                $"--template {key} does not build with its front end:\n{CliBuildE2E.Diagnostics(buildOutput)}");
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(work);
        }
    }

    // Split rather than passed as one string: RunProcess fills ArgumentList, which quotes each entry,
    // so a concatenated command line would arrive as a single argument npm does not recognise.
    private static Task<(int Exit, string Output)> Npm(string arguments, string workingDirectory) =>
        CliBuildE2E.RunProcess(
            "npm", arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries), workingDirectory);

    private static string Tail(string output)
    {
        var lines = output.Split('\n');
        return string.Join('\n', lines[Math.Max(0, lines.Length - 25)..]);
    }
}
