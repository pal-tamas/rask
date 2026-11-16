using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Forms;
using Rask.Core.Globalization;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;
using Rask.Server.Tests.WebSockets;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Server.Tests.App;

/// <summary>
///     <c>AddRask(culture: "hu")</c>: the one line a single-language app writes (#1289), and the same answer a
///     <see cref="RaskApp" /> gives for <c>c.Cultures.Add("hu")</c>.
/// </summary>
/// <remarks>
///     In the RaskApp collection because two of these read <see cref="RaskValidation.AutoValidate" />, a process-wide
///     static that building a RaskApp writes.
/// </remarks>
[Collection(RaskAppCollection.Name)]
public sealed class AppCultureLineTests
{
    [Fact]
    public async Task The_culture_named_in_the_line_is_the_request_culture_whatever_the_browser_asks_for()
    {
        using var host = RaskTestHost.Create<LanguagePage>(culture: "hu");
        host.Http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en"));

        var html = await host.Http.GetStringAsync("/", TestContext.Current.CancellationToken);

        Assert.Contains("lang=\"hu\"", html, StringComparison.Ordinal);
        // Values are written as the language's country writes them, the text in the language itself (#1305).
        Assert.Contains("<p>hu-HU|hu</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_forms_validation_message_comes_out_of_the_hungarian_catalog_under_the_line()
    {
        // The app ships the kit, whose catalog carries the Hungarian: loading it is what referencing Rask.Ui does.
        RuntimeHelpers.RunModuleConstructor(typeof(UiButton).Module.ModuleHandle);
        using var host = RaskTestHost.Create<EditTankApp>(diffMode: LiveDiffMode.DisabledFull, culture: "hu");
        var (page, ws) = await UnreadValueSubmitTests.OpenAsync(host);
        await using var connection = ws;

        await ws.SendJsonAsync(new
        {
            type = "batch",
            events = new[] { UnreadValueSubmitTests.Change(page, "weight", "6500.5"), UnreadValueSubmitTests.Submit(page, seq: 1) },
        });
        var html = await UnreadValueSubmitTests.ReadUntilAckAsync(ws);

        // As it is written into the page: the HTML encoder escapes the accented letters.
        Assert.Contains(System.Text.Encodings.Web.HtmlEncoder.Default.Encode("Egész számot adjon meg."), html, StringComparison.Ordinal);
        Assert.DoesNotContain("Enter a whole number.", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_line_leaves_forms_validating_their_models_by_themselves()
    {
        RaskValidation.AutoValidate = true;
        var services = new ServiceCollection();

        services.AddRask(culture: "hu");

        Assert.True(RaskValidation.AutoValidate);
    }

    [Fact]
    public void The_line_keeps_an_app_that_turned_automatic_validation_off_as_it_was()
    {
        RaskValidation.AutoValidate = false;
        var services = new ServiceCollection();

        try
        {
            services.AddRask(culture: "hu");

            Assert.False(RaskValidation.AutoValidate);
        }
        finally
        {
            RaskValidation.AutoValidate = true;
        }
    }

    [Fact]
    public void The_line_and_a_raskapp_naming_the_same_culture_end_with_the_same_options()
    {
        using var hand = new ServiceCollection().AddRask(culture: "hu").BuildServiceProvider();
        var app = RaskApp.Create([], b => b.WebHost.UseSetting("urls", "http://127.0.0.1:0"));
        app.Configure(c => c.Cultures.Add("hu"));

        var byHand = hand.GetRequiredService<RaskCultureOptions>();
        var byApp = app.Build<MinimalApp>().Services.GetRequiredService<RaskCultureOptions>();

        Assert.Equal(["hu"], byHand.SupportedCultures);
        Assert.Equal("hu", byHand.DefaultCulture);
        Assert.Equal(byHand.SupportedCultures, byApp.SupportedCultures);
        Assert.Equal(byHand.DefaultCulture, byApp.DefaultCulture);
    }

    [Fact]
    public void A_language_added_alongside_the_line_joins_it_and_the_line_stays_the_default()
    {
        using var services = new ServiceCollection()
            .AddRask(configureCulture: c => c.SupportedCultures.Add("en"), culture: "hu")
            .BuildServiceProvider();

        var options = services.GetRequiredService<RaskCultureOptions>();

        Assert.Equal(["hu", "en"], options.SupportedCultures);
        Assert.Equal("hu", options.DefaultCulture);
    }

    private sealed class LanguagePage : Component
    {
        protected override Component Render() => Div[P[$"{Culture.Name}|{UICulture.Name}"]];
    }
}
