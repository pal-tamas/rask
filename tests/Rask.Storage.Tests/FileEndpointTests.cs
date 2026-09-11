using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Storage.Tests;

[Collection(StorageDbCollection.Name)]
public sealed class FileEndpointTests
{
    [Fact]
    public async Task A_public_file_is_served_inline_with_every_safety_header_and_long_caching()
    {
        await using var host = await FileHost.StartAsync();
        var bytes = Samples.Png(2048);
        var file = await host.Files.SaveAsync(new MemoryStream(bytes), "photo.png", o => o.Public = true);

        using var response = await host.Client.GetAsync(host.Files.Url(file.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("photo.png", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Contains("sandbox", Header(response, "Content-Security-Policy"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Equal("public, max-age=31536000, immutable", Header(response, "Cache-Control"));
        Assert.Equal("\"" + file.Sha256 + "\"", response.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task A_private_file_is_not_on_the_public_route()
    {
        await using var host = await FileHost.StartAsync();
        var file = await host.Files.SaveAsync(new MemoryStream(Samples.Png()), "private.png");

        using var response = await host.Client.GetAsync(host.Files.Url(file.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("no-store", Header(response, "Cache-Control"));
    }

    [Fact]
    public async Task A_temporary_url_serves_a_private_file_privately()
    {
        await using var host = await FileHost.StartAsync();
        var file = await host.Files.SaveAsync(new MemoryStream(Samples.Png()), "private.png");

        using var response = await host.Client.GetAsync(await host.Files.TemporaryUrlAsync(file.Id, TimeSpan.FromMinutes(5)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
    }

    [Fact]
    public async Task Tampered_unknown_and_deleted_all_answer_the_same_404()
    {
        await using var host = await FileHost.StartAsync();
        var file = await host.Files.SaveAsync(new MemoryStream(Samples.Png()), "a.png");
        var url = (await host.Files.TemporaryUrlAsync(file.Id, TimeSpan.FromMinutes(5)))!;
        var tampered = url[..^3] + (url[^3] == 'A' ? "B" : "A") + url[^2..];
        var unknown = await host.Files.TemporaryUrlAsync(
            (await host.Files.SaveAsync(new MemoryStream(Samples.Png()), "b.png")).Id, TimeSpan.FromMinutes(5));

        using var bad = await host.Client.GetAsync(tampered);
        using var garbage = await host.Client.GetAsync("/_rask/files/not-a-token");
        using var tooLong = await host.Client.GetAsync("/_rask/files/" + new string('A', 300));
        await host.Files.DeleteAsync(file.Id);
        using var deleted = await host.Client.GetAsync(url);

        foreach (var response in new[] { bad, garbage, tooLong, deleted })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        }

        Assert.NotNull(unknown);
    }

    [Fact]
    public async Task Html_downloads_as_an_opaque_attachment()
    {
        await using var host = await FileHost.StartAsync();
        var file = await host.Files.SaveAsync(new MemoryStream(Samples.Html), "page.png", o => o.Public = true);

        using var response = await host.Client.GetAsync(host.Files.Url(file.Id));

        Assert.Equal("text/html", file.ContentType);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
    }

    [Fact]
    public async Task Ranges_conditional_requests_and_head_are_honoured()
    {
        await using var host = await FileHost.StartAsync();
        var bytes = Samples.Png(1000);
        var file = await host.Files.SaveAsync(new MemoryStream(bytes), "a.png", o => o.Public = true);
        var url = host.Files.Url(file.Id);

        using var range = new HttpRequestMessage(HttpMethod.Get, url);
        range.Headers.Range = new RangeHeaderValue(10, 19);
        using var partial = await host.Client.SendAsync(range);
        Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
        Assert.Equal(bytes[10..20], await partial.Content.ReadAsByteArrayAsync());

        using var conditional = new HttpRequestMessage(HttpMethod.Get, url);
        conditional.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"" + file.Sha256 + "\""));
        using var notModified = await host.Client.SendAsync(conditional);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);

        using var head = await host.Client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(bytes.Length, head.Content.Headers.ContentLength);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Download_serves_any_file_from_the_apps_own_endpoint()
    {
        await using var host = await FileHost.StartAsync();
        var file = await host.Files.SaveAsync(new MemoryStream("%PDF-1.7\n"u8.ToArray()), "invoice.pdf");

        using var response = await host.Client.GetAsync($"/download/{file.Id}");
        using var missing = await host.Client.GetAsync($"/download/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task A_file_route_wins_over_a_catch_all_under_the_same_prefix()
    {
        await using var host = await FileHost.StartAsync(app => app.MapGet("/_rask/{**path}", () => "dashboard"));
        var file = await host.Files.SaveAsync(new MemoryStream(Samples.Png()), "a.png", o => o.Public = true);

        using var response = await host.Client.GetAsync(host.Files.Url(file.Id));

        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public void Mapping_without_the_services_names_the_registration()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();

        var ex = Assert.Throws<InvalidOperationException>(() => app.MapRaskStorage());
        Assert.Contains("AddRaskStorage<AppDbContext>()", ex.Message);
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values)
            ? string.Join(", ", values)
            : "";

    private sealed class FileHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly string _root;

        private FileHost(WebApplication app, string root)
        {
            _app = app;
            _root = root;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public IFiles Files => _app.Services.GetRequiredService<IFiles>();

        public static async Task<FileHost> StartAsync(Action<WebApplication>? map = null)
        {
            var root = Path.Combine(Path.GetTempPath(), $"rask-storage-host-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = root });
            builder.WebHost.UseTestServer();
            builder.Services.AddRouting();
            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            builder.Services.AddRaskStorage<StorageDbContext>(o =>
            {
                o.Disk.Root = Path.Combine(root, "files");
                o.DataVolume = Path.Combine(root, "no-such-volume");
            });
            builder.Services.AddDbContextFactory<StorageDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(root, "app.db")}"));

            var app = builder.Build();
            app.MapRaskStorage();
            app.MapGet("/download/{id:guid}", (Guid id, IFiles files) => files.Download(id));
            map?.Invoke(app);

            await using (var scope = app.Services.CreateAsyncScope())
            {
                await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<StorageDbContext>>().CreateDbContextAsync();
                await db.Database.EnsureCreatedAsync();
            }

            await app.StartAsync();
            return new FileHost(app, root);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
