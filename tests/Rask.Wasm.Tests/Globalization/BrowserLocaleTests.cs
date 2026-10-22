using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Globalization;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Globalization;

// dotnet-wasm starts in the BROWSER's locale. An app that configured no culture renders its baked page
// in English, so a Hungarian browser booting it must not turn the month titles Hungarian under an
// English <html lang> and English placeholders.
[Collection("WasmSession")]
public class BrowserLocaleTests
{
    [Fact]
    public async Task A_browser_in_another_locale_formats_as_the_document_does_when_no_culture_is_configured()
    {
        using var browser = ProcessCulture.Set("hu-HU");
        RaskCulture.ResetForTests();
        var (session, services) = WasmSessionHarness.NewSession<MonthTitle>(
            configure: s => s.AddRaskCulture(lifetime: ServiceLifetime.Singleton));

        WasmCultureSeeder.Seed(services);
        var html = Html(await session.InitialRenderAsync());

        Assert.Contains("<html lang=\"en\"", html, StringComparison.Ordinal);
        Assert.Equal("October 2026", MonthTitle.In(html));
        Assert.Equal("en", CultureInfo.DefaultThreadCurrentCulture?.Name);
        Assert.Equal("en", CultureInfo.DefaultThreadCurrentUICulture?.Name);
    }

    [Fact]
    public async Task A_configured_culture_still_formats_in_the_language_negotiation_chose()
    {
        using var browser = ProcessCulture.Set("en-US");
        try
        {
            var (session, services) = WasmSessionHarness.NewSession<MonthTitle>(
                configure: s => s.AddRaskCulture(c => c.SupportedCultures.Add("hu"), ServiceLifetime.Singleton));

            WasmCultureSeeder.Seed(services);
            var html = Html(await session.InitialRenderAsync());

            Assert.Contains("<html lang=\"hu\"", html, StringComparison.Ordinal);
            Assert.Equal("2026. október", MonthTitle.In(html));
        }
        finally
        {
            RaskCulture.ResetForTests();
        }
    }

    private static string Html(byte[] frame)
    {
        using var doc = JsonDocument.Parse(frame);
        return doc.RootElement.GetProperty("html").GetString()!;
    }
}
