using Rask.Core.Globalization;
using Rask.Core.Routing;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Globalization;

// The baked half of the same promise: the prerender runs on the build machine, whose locale is nobody's
// visitor's, so a Hungarian build machine must bake the same English the browser will boot into.
[Collection("RaskPrerenderEnvironment")]
public class BuildMachineLocaleTests
{
    [Fact]
    public async Task A_build_machine_in_another_locale_bakes_the_document_language_when_no_culture_is_configured()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rask-prerender-" + Guid.NewGuid().ToString("N")[..8]);
        using var buildMachine = ProcessCulture.Set("hu-HU");
        RaskCulture.ResetForTests();
        RouteRegistry.Replace(RouteGroup, [new RouteRegistration(typeof(MonthTitle), "/month", null)]);
        Environment.SetEnvironmentVariable(WasmPrerender.OutputVariable, dir);

        try
        {
            await WasmHostBuilder.CreateDefault().Run<MonthTitle>();

            var html = await File.ReadAllTextAsync(Path.Combine(dir, "month", "index.html"), TestContext.Current.CancellationToken);
            Assert.Equal("October 2026", MonthTitle.In(html));
        }
        finally
        {
            Environment.SetEnvironmentVariable(WasmPrerender.OutputVariable, null);
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    [Fact]
    public async Task A_configured_culture_is_baked_in_that_language_whatever_the_build_machine_speaks()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rask-prerender-" + Guid.NewGuid().ToString("N")[..8]);
        using var buildMachine = ProcessCulture.Set("en-US");
        RaskCulture.ResetForTests();
        RouteRegistry.Replace(RouteGroup, [new RouteRegistration(typeof(MonthTitle), "/month", null)]);
        Environment.SetEnvironmentVariable(WasmPrerender.OutputVariable, dir);

        try
        {
            await WasmHostBuilder.CreateDefault().UseCulture(c => c.SupportedCultures.Add("hu")).Run<MonthTitle>();

            var html = await File.ReadAllTextAsync(Path.Combine(dir, "month", "index.html"), TestContext.Current.CancellationToken);
            Assert.Equal("2026. október", MonthTitle.In(html));
            Assert.Contains("<html lang=\"hu\"", html, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(WasmPrerender.OutputVariable, null);
            RaskCulture.ResetForTests();
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    private const string RouteGroup = "BuildMachineLocale";
}
