using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Globalization;
using Rask.Core.Live;
using Rask.Wasm;

namespace Rask.Wasm.Invariant.Tests;

/// <summary>
///     An English-only browser app ships no ICU, and still writes money, numbers and dates as US English (#1305): what
///     the page shows is what the same app shows on the server, not the invariant culture's <c>¤19.99</c>.
/// </summary>
/// <remarks>
///     This assembly runs with <c>InvariantGlobalization</c> and <c>PredefinedCulturesOnly</c> off, as such an app is
///     published (see the project file), so every culture here is a name over the invariant culture's data.
/// </remarks>
[Collection(ProcessCulture.Name)]
public sealed class EnglishWithoutIcuTests : IDisposable
{
    private static readonly DateTime Moment = new(2026, 3, 14, 15, 4, 5);

    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;
    private readonly CultureInfo? _defaultCulture = CultureInfo.DefaultThreadCurrentCulture;
    private readonly CultureInfo? _defaultUICulture = CultureInfo.DefaultThreadCurrentUICulture;

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
        CultureInfo.DefaultThreadCurrentCulture = _defaultCulture;
        CultureInfo.DefaultThreadCurrentUICulture = _defaultUICulture;
        RaskCulture.ResetForTests();
    }

    [Fact]
    public void This_process_has_no_culture_data_as_an_english_only_browser_app_has_none()
    {
        var english = CultureInfo.GetCultureInfo("en");

        var price = 19.99m.ToString("C", english);

        Assert.False(RaskCultureResolver.IsGlobalizationSupported);
        Assert.Equal("¤19.99", price);
    }

    [Fact]
    public void A_page_of_an_app_whose_cultures_are_english_alone_writes_money_and_dates_as_us_english()
    {
        using var services = English();
        var page = new Written();
        page.RenderHandle = new SessionHandle(services.GetRequiredService<IRaskCulture>());

        string html;
        using (LiveRenderContext.Begin(page))
        {
            html = page.ToHtml();
        }

        Assert.Equal("<p>$19.99 · -$1,234.50 · 3/14/2026 · March 2026 · 3:04\u202FPM · 12.500% · $1,764.6</p>", System.Net.WebUtility.HtmlDecode(html));
    }

    [Fact]
    public void The_process_of_an_app_whose_cultures_are_english_alone_formats_as_us_english_outside_a_render()
    {
        using var services = English();

        WasmCultureSeeder.PinSessionCulture(services);

        Assert.Equal("$19.99", 19.99m.ToString("C", CultureInfo.CurrentCulture));
        Assert.Equal("3/14/2026 3:04\u202FPM", Moment.ToString("g", CultureInfo.CurrentCulture));
        Assert.Equal("en", CultureInfo.CurrentUICulture.Name);
    }

    [Fact]
    public void A_handler_of_an_app_whose_cultures_are_english_alone_formats_as_us_english()
    {
        using var services = English();

        string price;
        using (RaskCultureScope.PushFrom(services))
        {
            price = $"{19.99m:C}";
        }

        Assert.Equal("$19.99", price);
    }

    [Fact]
    public void An_app_that_names_no_language_formats_as_us_english_too()
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        WasmCultureSeeder.PinWhenUnconfigured(services);

        Assert.Equal("$19.99", 19.99m.ToString("C", CultureInfo.CurrentCulture));
        Assert.Equal("Saturday, March 14, 2026", Moment.ToString("D", CultureInfo.CurrentCulture));
    }

    [Fact]
    public void Another_language_without_icu_still_formats_as_the_invariant_culture()
    {
        using var services = Cultures("hu");

        string price;
        using (RaskCultureScope.PushFrom(services))
        {
            price = $"{19.99m:C}";
        }

        Assert.Equal("¤19.99", price);
    }

    private static ServiceProvider English() => Cultures("en");

    private static ServiceProvider Cultures(string language)
    {
        RaskCulture.IsEnabled = true;
        var options = new RaskCultureOptions();
        options.SupportedCultures.Add(language);
        return new ServiceCollection()
            .AddSingleton(options)
            .AddSingleton<IRaskCulture>(new SessionCulture(options))
            .BuildServiceProvider();
    }

    private sealed class SessionHandle(IRaskCulture culture) : IRenderHandle
    {
        public Task RequestRender() => Task.CompletedTask;

        CultureInfo IRenderHandle.Culture => culture.Culture;

        CultureInfo IRenderHandle.UICulture => culture.UICulture;

        bool IRenderHandle.HasCulture => true;
    }

    private sealed class Written : Component
    {
        protected override Component? Render() =>
            P[$"{19.99m:C} · {-1234.5m:C} · {Moment:d} · {Moment:Y} · {Moment:t} · {0.125m:P} · {Rask.Format.Money(1764.6m)}"];
    }
}

/// <summary>The tests here move the process's culture, so they run one at a time.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessCulture
{
    public const string Name = "process culture";
}
