using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Rask.Core;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Server.Tests.Live;

/// <summary>
///     An app whose <c>Rask:Cultures</c> name languages alone writes money in each language's country's currency, on the
///     first response and on every live render after it (#1305): <c>{price:C}</c> was <c>¤19.99</c>.
/// </summary>
public sealed class CultureMoneyTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData("en", "en-US")]
    [InlineData("hu", "hu-HU")]
    public async Task The_first_response_writes_money_in_the_languages_country_and_keeps_the_language(string language, string country)
    {
        using var host = Host(language);

        var html = System.Net.WebUtility.HtmlDecode(await host.Http.GetStringAsync("/", TestContext.Current.CancellationToken));

        Assert.Contains($"<p id=\"price\">{Price(19.99m, country)}</p>", html, StringComparison.Ordinal);
        Assert.Contains($"<p id=\"language\">{language}</p>", html, StringComparison.Ordinal);
        Assert.Contains($"lang=\"{language}\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("¤", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "en-US")]
    [InlineData("hu", "hu-HU")]
    public async Task A_live_render_writes_money_in_the_languages_country_too(string language, string country)
    {
        using var host = Host(language);
        var html = System.Net.WebUtility.HtmlDecode(await host.Http.GetStringAsync("/", TestContext.Current.CancellationToken));
        var sessionId = MarkupAssert.SessionId(html);
        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, TestContext.Current.CancellationToken);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId, Wait);

        await ws.SendJsonAsync(new { id = MarkupAssert.FirstHandlerId(html), type = "click" }, ct: TestContext.Current.CancellationToken);
        var frame = await ws.TryReceiveTextAsync(Wait);

        Assert.NotNull(frame);
        using var doc = JsonDocument.Parse(frame);
        var rendered = System.Net.WebUtility.HtmlDecode(doc.RootElement.GetProperty("html").GetString());
        Assert.Contains($"<p id=\"price\">{Price(20.99m, country)}</p>", rendered, StringComparison.Ordinal);
        Assert.Contains($"<p id=\"language\">{language}</p>", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("¤", rendered, StringComparison.Ordinal);
    }

    private static string Price(decimal amount, string country) => amount.ToString("C", CultureInfo.GetCultureInfo(country));

    private static RaskTestHost Host(string language)
    {
        var host = RaskTestHost.Create<PricePage>(
            diffMode: LiveDiffMode.DisabledFull,
            configureCulture: c =>
            {
                c.SupportedCultures.Add("en");
                c.SupportedCultures.Add("hu");
            });
        host.Http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));
        return host;
    }

    private sealed class PricePage : Component
    {
        private decimal _price = 19.99m;

        protected override Component Render() =>
            Div[
                Button.OnClick(() => _price++)["Dearer"],
                P.Id("price")[$"{_price:C}"],
                P.Id("language")[UICulture.Name]
            ];
    }
}
