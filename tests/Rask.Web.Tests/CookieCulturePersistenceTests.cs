using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Rask.Core.Globalization;

namespace Rask.Web.Tests;

// The chosen culture, remembered in the cookie ASP.NET reads — written exactly as the old ICookies wrapper wrote it, so
// a visitor's existing cookie keeps working.
public sealed class CookieCulturePersistenceTests
{
    [Fact]
    public async Task The_choice_is_written_as_ASP_NETs_culture_cookie_for_a_year_across_the_site()
    {
        using var document = Document.Fake();
        var persistence = new CookieCulturePersistence(new ServiceCollection().BuildServiceProvider(), new RaskCultureOptions());

        await persistence.SaveAsync("hu-HU", "en-US", TestContext.Current.CancellationToken);

        var write = document.Calls.Single();
        Assert.Equal("cookie=", write.Member);
        Assert.Equal(".AspNetCore.Culture=c%3Dhu-HU%7Cuic%3Den-US; max-age=31536000; path=/; samesite=lax", write.Args.Single());
    }

    [Fact]
    public async Task A_renamed_cookie_lives_as_long_as_the_app_configured()
    {
        using var document = Document.Fake();
        var options = new RaskCultureOptions { CookieName = "lang", CookieMaxAgeDays = 1 };
        var persistence = new CookieCulturePersistence(new ServiceCollection().BuildServiceProvider(), options);

        await persistence.SaveAsync("de", "de", TestContext.Current.CancellationToken);

        Assert.Equal("lang=c%3Dde%7Cuic%3Dde; max-age=86400; path=/; samesite=lax", document.Calls.Single().Args.Single());
    }

    [Fact]
    public async Task The_cookie_reaches_the_sessions_page_even_outside_an_event_handler()
    {
        var browser = new FakeBrowser();
        var services = new ServiceCollection().AddSingleton<IJSRuntime>(browser).BuildServiceProvider();
        var persistence = new CookieCulturePersistence(services, new RaskCultureOptions());

        await persistence.SaveAsync("hu", "hu", TestContext.Current.CancellationToken);

        Assert.Equal(["__raskWeb.run"], browser.Calls.Select(c => c.Identifier));
        Assert.Contains(".AspNetCore.Culture=c%3Dhu%7Cuic%3Dhu", browser.Steps(0), StringComparison.Ordinal);
    }
}
