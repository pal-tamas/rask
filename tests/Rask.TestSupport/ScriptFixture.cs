using System.Text.Json;
using Rask.TestSupport.ScriptFixtures;
using Xunit;

namespace Rask.TestSupport;

/// <summary>
///     Runs one of a test project's <c>*Fixture.ts</c> harnesses in-process on Jint, a JavaScript engine written in
///     .NET, and returns the value it reports.
/// </summary>
/// <remarks>
///     <para>
///         Each fixture drives a real shipped module — the morph, the diff codec, the boot script — against a stub
///         DOM, in states a browser only reaches through genuine user interaction. The module arrives by
///         <c>import</c>, and MSBuild bundles the fixture into <c>script-fixtures/</c> beside the test assembly
///         during the build, so a fixture that no longer compiles fails the build rather than a test.
///     </para>
///     <para>
///         Nothing outside .NET is needed to run one: no Node, no browser. The engine gets the web globals a fixture
///         and its module reach for (timers, events, encoders, streams; see <c>web-globals.js</c>), its timers run on
///         a virtual clock, and <c>fixture.ts</c> is how a fixture reads its arguments and reports.
///     </para>
/// </remarks>
public static class ScriptFixture
{
    /// <summary>Runs <paramref name="name" /> with <paramref name="arguments" /> and parses what it reported.</summary>
    public static JsonElement Run(string name, params string[] arguments)
    {
        var script = ScriptPath(name);
        Assert.True(
            File.Exists(script),
            $"'{script}' is missing. It is bundled from {name}.ts by the _RaskBundleScriptFixtures target — "
            + "build the test project first.");

        var run = new FixtureRun(arguments);
        var (json, failure) = run.Run(script);
        Assert.True(
            failure is null,
            $"{name}: {failure}{Environment.NewLine}console:{Environment.NewLine}{run.Console}");

        // Cloned, so the element outlives the JsonDocument this method disposes.
        using var document = JsonDocument.Parse(json!);
        return document.RootElement.Clone();
    }

    private static string ScriptPath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "script-fixtures", name + ".mjs");
}
