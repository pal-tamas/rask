using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Globalization;

namespace Rask.Wasm.Tests.Globalization;

// An app's own resources (#1325) reach a WASM app only as satellite assemblies the runtime is asked to fetch: the
// reader's language before the first render, the app's others after it, each once. Off the browser the import
// records what it was asked. Serialised with every session boot: both share the import's record.
[Collection("WasmSession")]
public sealed class SatelliteLoadTests : IDisposable
{
    public SatelliteLoadTests()
    {
        WasmSatellites.Reset();
        lock (JSInterop.LoadedSatellites)
        {
            JSInterop.LoadedSatellites.Clear();
        }
    }

    public void Dispose() => WasmSatellites.Reset();

    private static IServiceProvider App(string reader, params string[] supported)
    {
        var options = new RaskCultureOptions();
        foreach (var name in supported)
        {
            options.SupportedCultures.Add(name);
        }

        var culture = new SessionCulture(options);
        var language = CultureInfo.GetCultureInfo(reader);
        culture.Seed(new CultureNegotiation(language, language, CultureSource.Default));
        return new ServiceCollection().AddSingleton(options).AddSingleton<IRaskCulture>(culture).BuildServiceProvider();
    }

    [Fact]
    public async Task The_readers_language_and_the_one_it_falls_back_to_are_fetched_before_the_first_render()
    {
        var services = App("hu-HU", "en", "hu-HU");

        await WasmSatellites.LoadReaders(services);

        var asked = Assert.Single(JSInterop.LoadedSatellites);
        Assert.Equal(["hu-HU", "hu"], asked);
    }

    [Fact]
    public async Task The_apps_other_languages_follow_and_none_is_fetched_twice()
    {
        var services = App("hu-HU", "en", "hu-HU");
        await WasmSatellites.LoadReaders(services);

        await WasmSatellites.LoadOthers(services);
        await WasmSatellites.LoadOthers(services);

        Assert.Equal(2, JSInterop.LoadedSatellites.Count);
        Assert.Equal(["en"], JSInterop.LoadedSatellites[1]);
    }

    [Fact]
    public async Task An_app_with_no_languages_asks_for_none_beyond_the_readers()
    {
        var services = new ServiceCollection().BuildServiceProvider();

        await WasmSatellites.LoadOthers(services);

        Assert.Empty(JSInterop.LoadedSatellites);
    }
}
