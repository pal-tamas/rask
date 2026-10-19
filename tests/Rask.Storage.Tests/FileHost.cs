using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Storage.Tests;

/// <summary>A test server with storage mapped over a real SQLite file and a real directory.</summary>
internal sealed class FileHost : IAsyncDisposable
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

    public IServiceProvider Services => _app.Services;

    public static async Task<FileHost> StartAsync(Action<WebApplication>? map = null, Action<StorageOptions>? configure = null)
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
            configure?.Invoke(o);
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
