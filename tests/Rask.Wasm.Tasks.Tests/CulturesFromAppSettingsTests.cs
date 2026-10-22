using System.Diagnostics;
using System.Text.Json;

namespace Rask.Wasm.Tasks.Tests;

/// <summary>
///     Evaluates <c>Rask.Wasm.targets</c> for real against an app's <c>wwwroot/appsettings.json</c>: the languages
///     its <c>Rask:Cultures</c> names are what the bundle is built with, and decide whether ICU ships.
/// </summary>
public sealed class CulturesFromAppSettingsTests
{
    [Fact]
    public async Task A_language_other_than_english_ships_ICU()
    {
        using var app = new TempApp("""{ /* the languages */ "Rask": { "Cultures": [ "en", "hu" ] } }""");

        var properties = await app.Evaluate();

        Assert.Equal("en,hu", properties["_RaskCultures"]);
        Assert.Equal("true", properties["RaskGlobalization"]);
        Assert.Equal("false", properties["InvariantGlobalization"]);
        Assert.Equal("false", properties["PredefinedCulturesOnly"]);
    }

    [Fact]
    public async Task English_alone_ships_no_ICU()
    {
        using var app = new TempApp("""{ "Rask": { "Cultures": [ "en", "en-GB" ] } }""");

        var properties = await app.Evaluate();

        Assert.Equal("en,en-GB", properties["_RaskCultures"]);
        Assert.Equal("false", properties["RaskGlobalization"]);
        Assert.Equal("true", properties["InvariantGlobalization"]);
    }

    [Fact]
    public async Task The_object_form_of_the_section_is_read_too()
    {
        using var app = new TempApp("""{ "Rask": { "Cultures": { "SupportedCultures": [ "de" ], "DefaultCulture": "de" } } }""");

        var properties = await app.Evaluate();

        Assert.Equal("de", properties["_RaskCultures"]);
        Assert.Equal("true", properties["RaskGlobalization"]);
    }

    [Fact]
    public async Task An_app_with_no_settings_file_names_no_language_and_ships_no_ICU()
    {
        using var app = new TempApp(settings: null);

        var properties = await app.Evaluate();

        Assert.Equal("", properties["_RaskCultures"]);
        Assert.Equal("false", properties["RaskGlobalization"]);
    }

    [Fact]
    public async Task Turning_ICU_off_by_hand_is_still_obeyed()
    {
        using var app = new TempApp("""{ "Rask": { "Cultures": [ "hu" ] } }""");

        var properties = await app.Evaluate("-p:RaskGlobalization=false");

        Assert.Equal("false", properties["RaskGlobalization"]);
        Assert.Equal("false", properties["_RaskAppGlobalization"]);
        Assert.Equal("true", properties["_RaskCulturesNeedIcu"]);
    }

    private sealed class TempApp : IDisposable
    {
        private static readonly string _targets = Path.Combine(
            RepoRoot(), "src", "Rask.Wasm", "build", "Rask.Wasm.targets");

        private readonly string _dir = Path.Combine(Path.GetTempPath(), "rask-cultures-" + Guid.NewGuid().ToString("N")[..8]);

        public TempApp(string? settings)
        {
            Directory.CreateDirectory(Path.Combine(_dir, "wwwroot"));
            File.WriteAllText(Path.Combine(_dir, "app.proj"), $"""
                <Project>
                  <PropertyGroup><RaskWasm>true</RaskWasm></PropertyGroup>
                  <Import Project="{_targets}" />
                </Project>
                """);
            if (settings is not null)
            {
                File.WriteAllText(Path.Combine(_dir, "wwwroot", "appsettings.json"), settings);
            }
        }

        public async Task<Dictionary<string, string>> Evaluate(params string[] extra)
        {
            string[] names =
            [
                "_RaskCultures", "_RaskCulturesNeedIcu", "_RaskAppGlobalization", "RaskGlobalization",
                "InvariantGlobalization", "PredefinedCulturesOnly",
            ];
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = _dir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add("msbuild");
            start.ArgumentList.Add("app.proj");
            start.ArgumentList.Add("-nologo");
            foreach (var arg in extra)
            {
                start.ArgumentList.Add(arg);
            }

            foreach (var name in names)
            {
                start.ArgumentList.Add("-getProperty:" + name);
            }

            using var process = Process.Start(start)!;
            var output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.True(process.ExitCode == 0, output);

            using var json = JsonDocument.Parse(output);
            return json.RootElement.GetProperty("Properties").EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.Ordinal);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
            {
                dir = dir.Parent;
            }

            return dir!.FullName;
        }
    }
}
