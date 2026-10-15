using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Rask.Server.Http;

namespace Rask.Server.Tests.Endpoints;

/// <summary>
///     The app's compiled stylesheet goes out the way Rask's runtime does — compressed, revalidated by its ETag, kept a
///     year under <c>?v={hash}</c> — even from a hand-wired host that serves its own static files.
/// </summary>
/// <remarks>
///     Left to <c>UseStaticFiles()</c>, a Tailwind + Rask.Ui sheet (650–750 KB) crossed the wire uncompressed and
///     with no cache header on every page load of the apps this was found on.
/// </remarks>
public sealed class AppStylesheetTests : IDisposable
{
    private const string Css = ".a{color:red}.b{color:blue}.c{margin:0}.d{padding:0}.e{display:flex}.f{gap:1rem}";

    private readonly string _webRoot = Directory.CreateTempSubdirectory("rask-sheet-").FullName;

    public AppStylesheetTests() => WriteSheet(Css);

    public void Dispose() => Directory.Delete(_webRoot, recursive: true);

    [Fact]
    public async Task A_hand_wired_host_serving_its_own_static_files_sends_the_sheet_compressed_and_revalidated()
    {
        await using var app = await HostAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/css/app.css");
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));

        using var response = await app.GetTestClient().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("br", Assert.Single(response.Content.Headers.ContentEncoding));
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoCache, "the bare address must be revalidated");
        Assert.NotNull(response.Headers.ETag);
        Assert.Equal(Css, await Brotli(response));
    }

    [Fact]
    public async Task The_address_that_names_the_hash_is_kept_for_a_year()
    {
        await using var app = await HostAsync();
        var hash = app.Services.GetRequiredService<AppStylesheet>().Hash;

        using var response = await app.GetTestClient().GetAsync($"/css/app.css?v={hash}", TestContext.Current.CancellationToken);

        Assert.Equal(HashedAsset.HashOf(System.Text.Encoding.UTF8.GetBytes(Css)), hash);
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
        Assert.Equal(Css, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_unchanged_sheet_answers_a_revalidation_with_304_and_no_body()
    {
        await using var app = await HostAsync();
        var client = app.GetTestClient();
        using var first = await client.GetAsync("/css/app.css", TestContext.Current.CancellationToken);
        using var again = new HttpRequestMessage(HttpMethod.Get, "/css/app.css");
        again.Headers.IfNoneMatch.Add(first.Headers.ETag!);

        using var response = await client.SendAsync(again, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_rebuilt_sheet_gets_a_new_hash()
    {
        var sheet = new AppStylesheet(new PhysicalFileProvider(_webRoot), "css/app.css", TimeSpan.Zero);
        var before = sheet.Hash;

        WriteSheet(Css + ".g{opacity:0}");

        Assert.NotNull(before);
        Assert.NotEqual(before, sheet.Hash);
    }

    [Fact]
    public async Task An_app_with_no_sheet_leaves_the_address_to_whatever_comes_after()
    {
        File.Delete(Path.Combine(_webRoot, "css", "app.css"));
        await using var app = await HostAsync();

        using var response = await app.GetTestClient().GetAsync("/css/app.css", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void A_RaskApp_document_links_the_sheet_under_its_hash()
    {
        var constructor = typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!;
        var styled = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("Styled" + Guid.NewGuid().ToString("N")[..8]), AssemblyBuilderAccess.Run,
            [new CustomAttributeBuilder(constructor, [RaskDocument.StylesheetMetadata, "css/app.css"])]);

        var defaults = RaskDocument.For(styled, kit: true, appStylesheetVersion: () => "0123456789abcdef");

        Assert.Equal("css/app.css", defaults.AppStylesheet);
        Assert.Equal("0123456789abcdef", defaults.AppStylesheetVersion?.Invoke());
    }

    // Ta's and Eszaf's shape: AddRask, then the app's OWN UseStaticFiles() — the sheet's middleware is ahead of it.
    private async Task<WebApplication> HostAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { WebRootPath = _webRoot });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(new AppStylesheet(new PhysicalFileProvider(_webRoot), "css/app.css", TimeSpan.Zero));
        builder.Services.AddRaskServer();
        var app = builder.Build();
        app.UseStaticFiles();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private void WriteSheet(string css)
    {
        Directory.CreateDirectory(Path.Combine(_webRoot, "css"));
        File.WriteAllText(Path.Combine(_webRoot, "css", "app.css"), css);
    }

    private static async Task<string> Brotli(HttpResponseMessage response)
    {
        await using var compressed = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        await using var brotli = new BrotliStream(compressed, CompressionMode.Decompress);
        using var reader = new StreamReader(brotli);
        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }
}
