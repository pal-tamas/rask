using System.Net;
using System.Text.RegularExpressions;
using Rask.Cli.Scaffolding;

namespace Rask.Templates.E2E.Tests;

/// <summary>
///     Each front-end template is scaffolded, published with its client, linted, and asked for its page
///     and its starter query.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="TemplateBuildE2ETests" /> compiles the C# half of these templates with the front end
///         off. This is the other half, and the only place it is run: the committed lockfile installs, the
///         TypeScript the host generates from its message records type-checks against the starter, the
///         bundler emits a page, and the published app serves both that page and the query it asks for.
///     </para>
///     <para>
///         One <c>dotnet publish</c> does the install and both builds, because that is the command a
///         deploy runs: <c>Rask.Spa.Hosting</c>'s targets own the chain, and driving npm by hand would
///         lint and bundle a client whose <c>src/rask/</c> was never written.
///     </para>
///     <para>
///         A template is minutes, so CI runs one job per template
///         (<c>scripts/run-template-e2e.sh --front-end=&lt;key&gt;</c>, through <see cref="TemplateSelection" />).
///     </para>
/// </remarks>
public sealed partial class FrontEndBuildE2ETests
{
    private const string SkipReason =
        "Front-end template gate: set RASK_TEMPLATE_FRONTEND_E2E=1 (with RASK_TEMPLATE_E2E=1) to run it. "
        + "Each case is a real npm ci, a production bundle and a published host — minutes per template. "
        + "See scripts/run-template-e2e.sh --front-end=<key>.";

    private static bool Enabled =>
        TemplateBuildE2ETests.Enabled
        && Environment.GetEnvironmentVariable("RASK_TEMPLATE_FRONTEND_E2E") == "1";

    public static TheoryData<string> FrontEnds() =>
        [.. TemplateSelection.Apply(SpaFramework.All.Select(framework => framework.Key))];

    [Theory]
    [MemberData(nameof(FrontEnds))]
    public async Task A_front_end_template_publishes_lints_and_serves_its_starter(string key)
    {
        Assert.SkipUnless(Enabled, SkipReason);
        var (feed, version) = await CliBuildE2E.LocalFeed.Value;
        var name = "Fe" + key.Replace("-", "", StringComparison.Ordinal);
        var work = TemplateBuildE2ETests.NewWorkingDirectory();

        try
        {
            var project = Path.Combine(work, name);
            var client = Path.Combine(project, "client");
            var published = Path.Combine(work, "published");
            TemplateBuildE2ETests.Write(
                TemplateBuildE2ETests.Scaffold(key, project, name, version, islands: []), project, feed);

            var (publish, publishOutput) = await CliBuildE2E.RunDotnet(
                $"publish \"{Path.Combine(project, name + ".csproj")}\" -c Release -warnaserror -m:1 -o \"{published}\"");
            var (lint, lintOutput) = await Npm("run lint", client);
            var (format, formatOutput) = await Npm("run format:check", client);

            Assert.True(publish == 0, $"--template {key} does not publish:\n{CliBuildE2E.Diagnostics(publishOutput)}");
            // `npm ci`, never `npm install`: the committed lockfile is the tree that is tested, and a
            // manifest that drifted from it fails here instead of being quietly re-resolved.
            Assert.Contains("Rask.Spa.Hosting: npm ci in", publishOutput, StringComparison.Ordinal);
            Assert.True(lint == 0, $"{key}: npm run lint failed\n{Tail(lintOutput)}");
            Assert.True(format == 0, $"{key}: npm run format:check failed\n{Tail(formatOutput)}");
            await AssertServesItsStarter(key, name, client, published);
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(work);
        }
    }

    /// <summary>The published app answers <c>/</c> with the bundle's page and the starter's query with a greeting.</summary>
    private static async Task AssertServesItsStarter(string key, string name, string client, string published)
    {
        var index = await File.ReadAllTextAsync(Path.Combine(published, "wwwroot", "index.html"));
        var script = ModuleScript().Match(index);
        var css = string.Concat(Directory
            .EnumerateFiles(Path.Combine(published, "wwwroot"), "*.css", SearchOption.AllDirectories)
            .Select(File.ReadAllText));
        var greeting = GreetingWireName().Match(
            await File.ReadAllTextAsync(Path.Combine(client, "src", "rask", "messages.ts")));
        Assert.True(script.Success, $"{key}: the published index.html loads no script:\n{index}");
        Assert.True(greeting.Success, $"{key}: the generated messages.ts declares no getGreeting.");
        // Every starter's root is `min-h-screen`. A Tailwind adapter the bundler never loaded leaves a
        // green build and an unstyled page (#839), and this rule is what is missing from it.
        Assert.True(css.Contains(".min-h-screen", StringComparison.Ordinal), $"{key}: the bundle's CSS has no Tailwind utilities.");

        await using var app = await ScaffoldedHost.StartPublishedAsync(published, name, TimeSpan.FromMinutes(2));
        using var http = new HttpClient { BaseAddress = new Uri(app.BaseUrl), Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.Add("X-Rask-Cqrs", "1");
        var cancellation = TestContext.Current.CancellationToken;

        var page = await http.GetStringAsync("/", cancellation);
        using var bundle = await http.GetAsync("/" + script.Groups[1].Value.TrimStart('/'), cancellation);
        // The request the starter makes on load, spelled the way the generated client spells it.
        using var answer = await http.GetAsync(
            $"/_rask/cqrs/request/{Uri.EscapeDataString(greeting.Groups[1].Value)}?m={Uri.EscapeDataString("""{"name":"world"}""")}",
            cancellation);
        var body = await answer.Content.ReadAsStringAsync(cancellation);

        Assert.Equal(index, page);
        Assert.True(bundle.StatusCode == HttpStatusCode.OK, $"{key}: {script.Groups[1].Value} answered {(int)bundle.StatusCode}.\n{app.Log}");
        Assert.True(answer.StatusCode == HttpStatusCode.OK, $"{key}: the starter query answered {(int)answer.StatusCode}: {body}\n{app.Log}");
        Assert.Contains("Hello, world!", body, StringComparison.Ordinal);
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

    [GeneratedRegex("""<script[^>]*\ssrc="([^"]+)"[^>]*>""")]
    private static partial Regex ModuleScript();

    [GeneratedRegex("""export const getGreeting = [^;]*?name: '([^']+)'""", RegexOptions.Singleline)]
    private static partial Regex GreetingWireName();
}
