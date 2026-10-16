using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Rask.Core;
using Rask.Server.Http;
using Rask.Server.Tests.Infrastructure;
using ServedSheet = Rask.Server.Http.AppStylesheet;

namespace Rask.Server.Tests.Endpoints;

/// <summary>
///     <c>AppStylesheet</c> in a hand-wired host's <c>HeadAssets</c> links the app's compiled sheet exactly as
///     <see cref="RaskApp" /> does: the same address, under the same content hash and path base.
/// </summary>
/// <remarks>
///     In the ScopedAssets collection, which runs alone: a path base is a process-wide static read at render time.
/// </remarks>
[Collection("ScopedAssets")]
public sealed partial class AppStylesheetEntryTests : IDisposable
{
    private const string Css = ".a{color:red}";

    private readonly string _webRoot = Directory.CreateTempSubdirectory("rask-entry-").FullName;

    public AppStylesheetEntryTests()
    {
        Directory.CreateDirectory(Path.Combine(_webRoot, "css"));
        File.WriteAllText(Path.Combine(_webRoot, "css", "app.css"), Css);
    }

    private static string Hash => HashedAsset.HashOf(System.Text.Encoding.UTF8.GetBytes(Css));

    public void Dispose() => Directory.Delete(_webRoot, recursive: true);

    [Fact]
    public async Task A_hand_wired_host_links_the_app_sheet_under_its_hash()
    {
        using var host = RaskTestHost.Create<LinkingApp>(ServeSheet);

        var html = await host.Http.GetStringAsync("/", TestContext.Current.CancellationToken);

        Assert.Contains($"href=\"/css/app.css?v={Hash}\"", Assert.Single(AppSheetLinks(html)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Under_a_path_base_the_link_names_the_prefixed_address()
    {
        using var host = RaskTestHost.Create<LinkingApp>(ServeSheet, pathBase: "/appA");

        var html = await host.Http.GetStringAsync("/appA/", TestContext.Current.CancellationToken);

        Assert.Contains($"href=\"/appA/css/app.css?v={Hash}\"", Assert.Single(AppSheetLinks(html)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_link_is_the_one_a_RaskApp_page_writes()
    {
        var raskApp = StyledRaskApp<QuietApp>();
        using var host = RaskTestHost.Create<LinkingApp>(ServeSheet);

        var fromRaskApp = Assert.Single(AppSheetLinks(await PageAsync(raskApp)));
        var fromEntry = Assert.Single(AppSheetLinks(await host.Http.GetStringAsync("/", TestContext.Current.CancellationToken)));

        Assert.Equal(fromRaskApp, fromEntry);
    }

    [Fact]
    public async Task A_RaskApp_page_that_also_writes_it_links_the_sheet_once()
    {
        var raskApp = StyledRaskApp<LinkingApp>();

        var html = await PageAsync(raskApp);

        Assert.Contains($"href=\"/css/app.css?v={Hash}\"", Assert.Single(AppSheetLinks(html)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_host_that_serves_no_sheet_links_none()
    {
        using var host = RaskTestHost.Create<LinkingApp>(services => services.RemoveAll<Rask.Core.Components.IAppStylesheetSource>());

        var html = await host.Http.GetStringAsync("/", TestContext.Current.CancellationToken);

        Assert.Empty(AppSheetLinks(html));
    }

    private void ServeSheet(IServiceCollection services) =>
        services.AddSingleton(new ServedSheet(new PhysicalFileProvider(_webRoot), "css/app.css", TimeSpan.Zero));

    // RaskApp's document for an app whose build compiled css/app.css (Rask.Tailwind's metadata), kit off so the
    // page carries no other stylesheet link.
    private WebApplication StyledRaskApp<TApp>()
        where TApp : Component
    {
        var constructor = typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!;
        var styled = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("Styled" + Guid.NewGuid().ToString("N")[..8]), AssemblyBuilderAccess.Run,
            [new CustomAttributeBuilder(constructor, [RaskDocument.StylesheetMetadata, "css/app.css"])]);
        var sheet = new ServedSheet(new PhysicalFileProvider(_webRoot), "css/app.css", TimeSpan.Zero);

        return RaskApp.Create([], b =>
            {
                b.WebHost.UseSetting("urls", "http://127.0.0.1:0");
                b.Services.AddSingleton(sheet);
                b.Services.AddSingleton(RaskDocument.For(styled, kit: false, appStylesheetVersion: () => sheet.Hash));
            })
            .Configure(c => c.Ui.Off())
            .Build<TApp>();
    }

    private static List<string> AppSheetLinks(string html) =>
        [.. AppSheetLink().Matches(html).Select(m => m.Value)];

    private static async Task<string> PageAsync(WebApplication app)
    {
        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            var address = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            return await client.GetStringAsync("/", TestContext.Current.CancellationToken);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // The app sheet's link only: a page also links its scoped CSS (/_rask/a/{hash}.css), which is not this.
    [GeneratedRegex("<link [^>]*href=\"[^\"]*/css/app\\.css(\\?v=[^\"]*)?\"[^>]*>")]
    private static partial Regex AppSheetLink();
}

/// <summary>A hand-wired App that links its sheet itself.</summary>
public sealed partial class LinkingApp : Component
{
    protected override Component? HeadAssets => [Title["linking"], AppStylesheet];

    protected override Component? Render() => H1["ok"];
}

/// <summary>An App that leaves the sheet to its host.</summary>
public sealed partial class QuietApp : Component
{
    protected override Component? HeadAssets => Title["quiet"];

    protected override Component? Render() => H1["ok"];
}
