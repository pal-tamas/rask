using System.Net;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using ServedSheet = Rask.Server.Http.AppStylesheet;

namespace Rask.Server.Tests.App;

/// <summary>
///     Every stylesheet a <see cref="RaskApp" /> page links answers 200 <c>text/css</c>, whichever way the app takes the
///     UI kit: precompiled (no Tailwind build), compiled into its own sheet, written into wwwroot, or not at all.
/// </summary>
/// <remarks>
///     #1315: an app with the kit on, no Tailwind build and no <c>RaskUiWriteStylesheet</c> linked
///     <c>/css/rask-ui.css</c> and nothing served it — a 404 on every page and an unstyled kit. The host now answers
///     that address out of the kit's own assembly, so the link and the sheet cannot disagree.
/// </remarks>
[Collection(RaskAppCollection.Name)]
public sealed partial class RaskAppKitStylesheetTests : IDisposable
{
    private readonly string _webRoot = Directory.CreateTempSubdirectory("rask-kit-sheet-").FullName;

    public RaskAppKitStylesheetTests() => Directory.CreateDirectory(Path.Combine(_webRoot, "css"));

    public void Dispose() => Directory.Delete(_webRoot, recursive: true);

    [Fact]
    public async Task An_app_with_the_kit_and_no_Tailwind_build_serves_every_stylesheet_its_page_links()
    {
        var app = Create(_ => { });

        var visit = await VisitAsync(app);

        Assert.Contains(UiStylesheet.Href(), visit.Links);
        Assert.NotEmpty(visit.Answers);
        Assert.All(visit.Answers, answer => Assert.Equal((HttpStatusCode.OK, "text/css"), (answer.Status, answer.MediaType)));
    }

    [Fact]
    public async Task The_kit_sheet_it_serves_is_the_kits_own_bytes_kept_for_a_year_under_its_version()
    {
        var app = Create(_ => { });

        var visit = await VisitAsync(app);

        var kit = Assert.Single(visit.Answers, answer => answer.Href == UiStylesheet.Href());
        Assert.Equal(UiStylesheet.Css, kit.Body);
        Assert.Contains("immutable", kit.CacheControl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_app_that_wrote_the_precompiled_sheet_into_wwwroot_serves_the_kits_bytes_at_that_address()
    {
        // RaskUiWriteStylesheet's copy. A stale one — written by an older kit — must not outlive the upgrade.
        await File.WriteAllTextAsync(Path.Combine(_webRoot, "css", "rask-ui.css"), ".stale{color:red}", TestContext.Current.CancellationToken);
        var app = Create(_ => { });

        var visit = await VisitAsync(app);

        var kit = Assert.Single(visit.Answers, answer => answer.Href == UiStylesheet.Href());
        Assert.Equal((HttpStatusCode.OK, UiStylesheet.Css), (kit.Status, kit.Body));
    }

    [Fact]
    public async Task An_app_whose_sheet_compiles_the_kit_in_links_only_its_own_sheet_and_serves_it()
    {
        await File.WriteAllTextAsync(Path.Combine(_webRoot, "css", "app.css"), ".a{color:red}", TestContext.Current.CancellationToken);
        var compiledIn = AppWith(
            (RaskDocument.StylesheetMetadata, "css/app.css"), (RaskDocument.KitStylesheetMetadata, "compiled-in"));
        var sheet = new ServedSheet(new PhysicalFileProvider(_webRoot), "css/app.css", TimeSpan.Zero);
        var app = Create(services => services
            .AddSingleton(sheet)
            .AddSingleton(RaskDocument.For(compiledIn, kit: true, appStylesheetVersion: () => sheet.Hash)));

        var visit = await VisitAsync(app);

        Assert.DoesNotContain(visit.Links, link => link.Contains(UiStylesheet.Path, StringComparison.Ordinal));
        Assert.Contains(visit.Links, link => link.StartsWith("/css/app.css?v=", StringComparison.Ordinal));
        Assert.All(visit.Answers, answer => Assert.Equal((HttpStatusCode.OK, "text/css"), (answer.Status, answer.MediaType)));
    }

    [Fact]
    public async Task An_app_with_the_kit_off_links_no_kit_sheet_and_serves_every_sheet_it_does_link()
    {
        var app = Create(_ => { }, kit: false);

        var visit = await VisitAsync(app);

        Assert.DoesNotContain(visit.Links, link => link.Contains(UiStylesheet.Path, StringComparison.Ordinal));
        Assert.All(visit.Answers, answer => Assert.Equal((HttpStatusCode.OK, "text/css"), (answer.Status, answer.MediaType)));
    }

    private WebApplication Create(Action<IServiceCollection> services, bool kit = true) =>
        // The web root is host configuration, which only the arguments may set.
        RaskApp.Create(["--webroot", _webRoot], b =>
            {
                b.WebHost.UseSetting("urls", "http://127.0.0.1:0");
                services(b.Services);
            })
            .Configure(c =>
            {
                if (!kit)
                {
                    c.Ui.Off();
                }
            })
            .Build<MinimalApp>();

    private static AssemblyBuilder AppWith(params (string Key, string Value)[] metadata)
    {
        var constructor = typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!;
        return AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("App" + Guid.NewGuid().ToString("N")[..8]),
            AssemblyBuilderAccess.Run,
            metadata.Select(m => new CustomAttributeBuilder(constructor, [m.Key, m.Value])));
    }

    // The page, then every stylesheet it links, each asked for as a browser would.
    private static async Task<Visit> VisitAsync(WebApplication app)
    {
        var cancel = TestContext.Current.CancellationToken;
        await app.StartAsync(cancel);
        try
        {
            var address = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            var html = await client.GetStringAsync("/", cancel);
            var links = StylesheetLinks().Matches(html)
                .Select(m => WebUtility.HtmlDecode(m.Groups["href"].Value))
                .ToList();

            var answers = new List<Answer>();
            foreach (var href in links)
            {
                using var response = await client.GetAsync(href, cancel);
                answers.Add(new Answer(
                    href,
                    response.StatusCode,
                    response.Content.Headers.ContentType?.MediaType,
                    response.Headers.CacheControl?.ToString() ?? string.Empty,
                    await response.Content.ReadAsStringAsync(cancel)));
            }

            return new Visit(links, answers);
        }
        finally
        {
            await app.StopAsync(cancel);
            await app.DisposeAsync();
        }
    }

    [GeneratedRegex("<link (?=[^>]*rel=\"stylesheet\")[^>]*href=\"(?<href>[^\"]+)\"[^>]*>")]
    private static partial Regex StylesheetLinks();

    private sealed record Visit(List<string> Links, List<Answer> Answers);

    private sealed record Answer(string Href, HttpStatusCode Status, string? MediaType, string CacheControl, string Body);
}
