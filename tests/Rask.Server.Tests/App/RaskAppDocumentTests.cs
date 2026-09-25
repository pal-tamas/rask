using System.Reflection;
using System.Reflection.Emit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Server.Tests.App;

/// <summary>
///     A <see cref="RaskApp"/> writes the document around the App — the charset and viewport, the UI kit's
///     stylesheet and its theme scope on <c>&lt;html&gt;</c> — so an App is a title and a router.
/// </summary>
/// <remarks>
///     The theme scope is the one whose absence is silent: without it every kit component renders
///     structurally correct and completely grey, which only a page's own HTML can show.
/// </remarks>
[Collection(RaskAppCollection.Name)]
public sealed class RaskAppDocumentTests
{
    [Fact]
    public async Task An_App_that_writes_only_its_title_still_draws_with_the_kit()
    {
        var app = RaskApp.Create([], b => b.WebHost.UseSetting("urls", "http://127.0.0.1:0")).Build<MinimalApp>();

        var html = await PageAsync(app);

        Assert.Contains($"<html lang=\"en\" {UiStylesheet.ThemeScopeAttribute}", html, StringComparison.Ordinal);
        var charset = html.IndexOf("charset=\"utf-8\"", StringComparison.Ordinal);
        var kit = html.IndexOf(UiStylesheet.Href(), StringComparison.Ordinal);
        var title = html.IndexOf("rask-server-tests</title>", StringComparison.Ordinal);
        Assert.True(charset >= 0 && kit > charset && title > kit, $"expected charset, kit sheet, then title:\n{html}");
        Assert.Contains("name=\"viewport\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Turning_the_kit_off_keeps_the_charset_and_drops_the_kit()
    {
        var app = RaskApp.Create([], b => b.WebHost.UseSetting("urls", "http://127.0.0.1:0"))
            .Configure(c => c.Ui.Off())
            .Build<MinimalApp>();

        var html = await PageAsync(app);

        Assert.DoesNotContain(UiStylesheet.ThemeScopeAttribute, html, StringComparison.Ordinal);
        Assert.DoesNotContain(UiStylesheet.Href(), html, StringComparison.Ordinal);
        Assert.Contains("charset=\"utf-8\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_hand_wired_host_keeps_the_document_its_App_writes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRaskServer();
        var app = builder.Build();
        app.UseRouting();
        app.MapRask<MinimalApp>();
        await app.StartAsync();

        string html;
        try
        {
            html = await app.GetTestClient().GetStringAsync("/");
        }
        finally
        {
            await app.StopAsync();
        }

        Assert.DoesNotContain(UiStylesheet.ThemeScopeAttribute, html, StringComparison.Ordinal);
        Assert.DoesNotContain(UiStylesheet.Href(), html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_stylesheet_the_build_compiled_is_the_one_the_page_links()
    {
        var metadata = new CustomAttributeBuilder(
            typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!,
            [RaskDocument.StylesheetMetadata, "css/app.css"]);
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("StyledApp"), AssemblyBuilderAccess.Run, [metadata]);

        var styled = RaskDocument.For(assembly, kit: true);
        var unstyled = RaskDocument.For(typeof(MinimalApp).Assembly, kit: true);

        Assert.Equal("css/app.css", styled.AppStylesheet);
        Assert.Null(unstyled.AppStylesheet);
    }

    private static async Task<string> PageAsync(WebApplication app)
    {
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            return await client.GetStringAsync("/");
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
