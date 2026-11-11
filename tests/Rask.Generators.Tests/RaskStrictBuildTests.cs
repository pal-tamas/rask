using System.Diagnostics;
using System.Text.Json;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

/// <summary>
///     Builds a throwaway app against <c>build/Rask.Strict.targets</c>, imported from <c>obj/</c> exactly where NuGet
///     puts a package's targets — after the csproj, so a <c>&lt;RaskStrict&gt;</c> written there is seen — with the
///     RASK analyzers this suite builds.
/// </summary>
public sealed class RaskStrictBuildTests
{
    private const string NullableSlip = "public static class Names { public static string First() { string s = null; return s; } }";

    [Fact]
    public async Task An_app_that_does_not_opt_in_builds_exactly_as_before()
    {
        using var app = new ThrowawayApp("Shop", strict: null, nullable: "disable");

        var properties = await app.Evaluate("Nullable", "TreatWarningsAsErrors", "AnalysisLevel", "EnforceCodeStyleInBuild");

        Assert.Equal("disable", properties["Nullable"]);
        Assert.NotEqual("true", properties["TreatWarningsAsErrors"]);
        Assert.NotEqual("true", properties["EnforceCodeStyleInBuild"]);
        Assert.NotEqual("latest-recommended", properties["AnalysisLevel"]);
    }

    [Fact]
    public async Task At_errors_a_nullable_slip_fails_the_build()
    {
        using var app = new ThrowawayApp("Shop", strict: "errors", nullable: "disable");
        app.Write("Names.cs", NullableSlip);

        var (exitCode, output) = await app.Build();

        Assert.NotEqual(0, exitCode);
        Assert.Contains("error CS8600", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task At_warnings_the_same_slip_is_reported_and_the_build_still_passes()
    {
        using var app = new ThrowawayApp("Shop", strict: "warnings", nullable: "disable");
        app.Write("Names.cs", NullableSlip);

        var (exitCode, output) = await app.Build();

        Assert.True(exitCode == 0, output);
        Assert.Contains("warning CS8600", output, StringComparison.Ordinal);
        Assert.DoesNotContain("error CS", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unused_using_is_reported_by_the_build_and_a_shared_folder_is_not()
    {
        // IDE0005 runs in a build only with a documentation file, which Rask.Strict.props turns on (CS1591 stays
        // quiet); Features/Shared is where `rask new` keeps what features share, and CA1716 calls Shared a VB keyword.
        using var app = new ThrowawayApp("Shop", strict: "warnings", nullable: "enable");
        app.Write(".editorconfig", "root = true\n[*.cs]\ndotnet_diagnostic.IDE0005.severity = warning\n");
        app.Write("Money.cs", "using System.Text;\nnamespace Shop.Features.Shared;\npublic static class Money { public static int Zero => 0; }\n");

        var (exitCode, output) = await app.Build();

        Assert.True(exitCode == 0, output);
        Assert.Contains("warning IDE0005", output, StringComparison.Ordinal);
        Assert.DoesNotContain("CA1716", output, StringComparison.Ordinal);
        Assert.DoesNotContain("CS1591", output, StringComparison.Ordinal);
        Assert.DoesNotContain("EnableGenerateDocumentationFile", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_strict_test_project_reports_a_test_not_named_as_a_sentence()
    {
        using var tests = new ThrowawayApp("Shop.Tests", strict: "warnings", nullable: "enable");
        tests.Write("Fact.cs", "namespace Xunit { public sealed class FactAttribute : System.Attribute { } }");
        tests.Write("CartTests.cs", """
            namespace Shop.Tests;
            public sealed class CartTests
            {
                [Xunit.Fact] public void Add_puts_the_item_in_the_cart() { }
                [Xunit.Fact] public void AddItem() { }
            }
            """);

        var (exitCode, output) = await tests.Build();

        Assert.True(exitCode == 0, output);
        Assert.Contains("warning RASK116: 'AddItem'", output, StringComparison.Ordinal);
        Assert.DoesNotContain("'Add_puts_the_item_in_the_cart'", output, StringComparison.Ordinal);
        Assert.DoesNotContain("CA1707", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task At_errors_a_NuGet_audit_finding_stays_a_warning_beside_the_app_s_own_exceptions()
    {
        // Asserted on the evaluated property, not on a real NU1903: an advisory comes from nuget.org's vulnerability
        // feed, which a hermetic test cannot fake. NuGet reads WarningsNotAsErrors itself, so the property is the switch.
        using var app = new ThrowawayApp(
            "Shop", strict: "errors", nullable: "enable", "<WarningsNotAsErrors>CS0618</WarningsNotAsErrors>");
        using var warned = new ThrowawayApp("Shop", strict: "warnings", nullable: "enable");

        var errors = await app.Evaluate("WarningsNotAsErrors", "TreatWarningsAsErrors");
        var exceptions = errors["WarningsNotAsErrors"].Split(';');
        var atWarnings = (await warned.Evaluate("WarningsNotAsErrors", "TreatWarningsAsErrors"))["WarningsNotAsErrors"];

        Assert.Equal("true", errors["TreatWarningsAsErrors"]);
        Assert.Equal(
            ["CS0618", "NU1901", "NU1902", "NU1903", "NU1904"],
            exceptions.Where(code => code.StartsWith("CS", StringComparison.Ordinal) || code.StartsWith("NU", StringComparison.Ordinal)));
        Assert.DoesNotContain("NU190", atWarnings, StringComparison.Ordinal);
    }

    [Fact]
    public async Task At_errors_a_dependency_s_trim_findings_stay_warnings_and_the_app_s_own_do_not()
    {
        // The trimmer folds a referenced assembly's warnings into one IL2104 (IL3053 for AOT) and reports the app's own
        // assembly warning by warning; it reads WarningsNotAsErrors itself. Asserted on the property: a real publish
        // is minutes, and the summary needs a dependency that warns.
        using var app = new ThrowawayApp("Shop", strict: "errors", nullable: "enable");

        var exceptions = (await app.Evaluate("WarningsNotAsErrors", "TreatWarningsAsErrors"))["WarningsNotAsErrors"].Split(';');

        Assert.Contains("IL2104", exceptions);
        Assert.Contains("IL3053", exceptions);
        Assert.DoesNotContain("IL2026", exceptions);
        Assert.DoesNotContain("IL3050", exceptions);
    }

    [Fact]
    public async Task Only_a_test_project_gets_the_test_name_rule()
    {
        using var app = new ThrowawayApp("Shop", strict: "errors", nullable: "enable");
        using var tests = new ThrowawayApp("Shop.Tests", strict: "errors", nullable: "enable");

        var appConfigs = await app.EvaluateItems("EditorConfigFiles");
        var testConfigs = await tests.EvaluateItems("EditorConfigFiles");

        Assert.DoesNotContain(appConfigs, file => file.EndsWith("rask-strict-tests.globalconfig", StringComparison.Ordinal));
        Assert.Contains(testConfigs, file => file.EndsWith("rask-strict-tests.globalconfig", StringComparison.Ordinal));
    }

    private sealed class ThrowawayApp : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "rask-strict-" + Guid.NewGuid().ToString("N")[..8]);
        private readonly string _project;

        public ThrowawayApp(string name, string? strict, string nullable, string ownProperties = "")
        {
            _project = name + ".csproj";
            var root = RepoRoot();
            Directory.CreateDirectory(Path.Combine(_dir, "obj"));
            File.Copy(Path.Combine(root, "global.json"), Path.Combine(_dir, "global.json"));
            // Where the docs say to write it, so the test project beside the app gets it too.
            Write("Directory.Build.props", $"""
                <Project>
                  <PropertyGroup>
                    {(strict is null ? "" : $"<RaskStrict>{strict}</RaskStrict>")}
                  </PropertyGroup>
                </Project>
                """);
            Write(_project, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net11.0</TargetFramework>
                    <Nullable>{nullable}</Nullable>
                    {ownProperties}
                  </PropertyGroup>
                  <ItemGroup>
                    <Analyzer Include="{typeof(TestNameSentenceAnalyzer).Assembly.Location}" />
                  </ItemGroup>
                </Project>
                """);
            // Where NuGet writes a package's imports (obj/<project>.nuget.g.props and .targets): the SDK imports every
            // obj/<project>.*.props after Directory.Build.props, and every obj/<project>.*.targets after the project
            // file, before its own analyzer targets.
            Write(Path.Combine("obj", _project + ".rask.props"), $"""
                <Project>
                  <Import Project="{Path.Combine(root, "src", "Rask.Core", "build", "Rask.Strict.props")}" />
                </Project>
                """);
            Write(Path.Combine("obj", _project + ".rask.targets"), $"""
                <Project>
                  <Import Project="{Path.Combine(root, "src", "Rask.Core", "build", "Rask.Strict.targets")}" />
                </Project>
                """);
        }

        public void Write(string file, string content) => File.WriteAllText(Path.Combine(_dir, file), content);

        public async Task<(int ExitCode, string Output)> Build() =>
            await Dotnet("build", _project, "-nologo", "-m:1", "-nodeReuse:false", "-clp:NoSummary", "-v:q");

        public async Task<Dictionary<string, string>> Evaluate(params string[] names)
        {
            var (exitCode, output) = await Dotnet(["msbuild", _project, "-nologo", .. names.Select(n => "-getProperty:" + n)]);
            Assert.True(exitCode == 0, output);

            using var json = JsonDocument.Parse(output);
            return json.RootElement.GetProperty("Properties").EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.Ordinal);
        }

        public async Task<List<string>> EvaluateItems(string item)
        {
            var (exitCode, output) = await Dotnet("msbuild", _project, "-nologo", "-getItem:" + item);
            Assert.True(exitCode == 0, output);

            using var json = JsonDocument.Parse(output);
            return [.. json.RootElement.GetProperty("Items").GetProperty(item).EnumerateArray()
                .Select(i => i.GetProperty("FullPath").GetString() ?? "")];
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
        }

        private async Task<(int ExitCode, string Output)> Dotnet(params string[] args)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = _dir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in args)
            {
                start.ArgumentList.Add(arg);
            }

            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            return (process.ExitCode, await stdout + await stderr);
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new InvalidOperationException("Rask.slnx not found above the test output.");
        }
    }
}
