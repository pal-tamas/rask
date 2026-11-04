using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Globalization;
using Rask.Core.Routing;

namespace Rask.Wasm.Tests.Globalization;

// The browser boot of an app with languages in its URLs (#1199): the address decides the language before the first
// render, whatever the browser says. Off the browser the culture-signals import answers "{}", which is a visitor
// with nothing to go on. Serialised because seeding pins the process culture.
[Collection("RaskPrerenderEnvironment")]
public class UrlCultureSeedTests
{
    private static (IServiceProvider Services, RouteState Route) Boot(string path)
    {
        var options = new RaskCultureOptions { UrlPrefix = true, SupportedCultures = { "hu", "en", "de" } };
        var route = new RouteState { Languages = UrlLanguages.From(options) };
        route.Path = path;
        var services = new ServiceCollection()
            .AddSingleton(options)
            .AddSingleton(route)
            .AddSingleton<IRaskCulture>(new SessionCulture(options))
            .BuildServiceProvider();
        return (services, route);
    }

    [Fact]
    public void A_deep_link_boots_in_the_language_its_address_names()
    {
        using var browser = ProcessCulture.Set("de-DE");
        var (services, route) = Boot("/en/rolunk");

        var sendTo = WasmCultureSeeder.Seed(services);

        Assert.Null(sendTo);
        Assert.Equal("en", services.GetRequiredService<IRaskCulture>().UICulture.Name);
        Assert.Equal("/en/rolunk", route.Path);
    }

    [Fact]
    public void The_bare_root_with_nothing_to_go_on_boots_in_the_default_and_stays()
    {
        using var browser = ProcessCulture.Set("de-DE");
        var (services, route) = Boot("/");

        var sendTo = WasmCultureSeeder.Seed(services);

        Assert.Null(sendTo);
        Assert.Equal("hu", services.GetRequiredService<IRaskCulture>().UICulture.Name);
        Assert.Equal("/", route.Path);
    }
}
