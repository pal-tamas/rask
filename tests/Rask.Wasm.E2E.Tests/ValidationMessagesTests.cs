using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using Rask.Wasm.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Wasm.E2E.Tests;

/// <summary>
///     A WASM app's validation messages from its own resources, the .NET 11 way: <c>AddLocalization()</c> and
///     <c>AddValidation(o =&gt; o.LocalizerProvider = …)</c> in Program.cs, <c>Messages/ValidationMessages.hu.resx</c>
///     beside it. A Hungarian reader reads the app's sentence with the field's display name looked up in the same
///     resources; an English reader reads .NET's own. The Server's half is <c>AsyncAttributeTests</c> in
///     <c>Rask.Server.E2E.Tests</c>.
/// </summary>
/// <remarks>
///     Only a browser proves the resources reach the reader: the Hungarian satellite assembly has to be published,
///     downloaded and found by the resource manager inside the WebAssembly runtime.
/// </remarks>
public sealed class ValidationMessagesTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    // The first WASM boot downloads and starts the runtime.
    private static readonly LocatorAssertionsToHaveTextOptions Boot = new() { Timeout = 60_000 };
    private static readonly LocatorAssertionsToHaveTextOptions Settled = new() { Timeout = 15_000 };

    public static TheoryData<string, int> Readers => new()
    {
        { "en-US", 1280 }, { "en-US", 390 }, { "hu-HU", 1280 }, { "hu-HU", 390 },
    };

    [Theory]
    [MemberData(nameof(Readers))]
    public async Task A_hungarian_reader_reads_the_apps_own_resources_and_an_english_one_reads_dotnets_words(string locale, int width)
    {
        await using var host = await SignInHost.StartAsync();
        await using var context = await playwright.Browser.NewContextAsync(
            new() { Locale = locale, ViewportSize = new() { Width = width, Height = 800 } });
        var page = await context.NewPageAsync();
        await page.GotoAsync(host.BaseUrl + "/signup");
        await Expect(page.Locator("#save")).ToHaveTextAsync("save", Boot);

        await page.ClickAsync("#save");

        await Expect(page.Locator("#email-error")).ToHaveTextAsync(
            locale.StartsWith("hu", StringComparison.Ordinal) ? "A(z) E-mail-cím megadása kötelező." : "The E-mail field is required.",
            Settled);
    }
}
