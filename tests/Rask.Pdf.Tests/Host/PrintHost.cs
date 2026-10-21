using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Rask.Server;
using Rask.Server.Authentication;

namespace Rask.Printing.Tests.Host;

/// <summary>
/// A hand-wired Rask server — AddRask, AddRaskPdf, MapRask — with its own web root, and a way to call
/// <c>Pdf.From</c> as a given user would from a handler of theirs.
/// </summary>
internal sealed class PrintHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly DirectoryInfo _webRoot;

    private PrintHost(WebApplication app, DirectoryInfo webRoot)
    {
        _app = app;
        _webRoot = webRoot;
    }

    public IServiceProvider Services => _app.Services;

    /// <summary>Starts the host; <paramref name="files"/> are written into its web root first.</summary>
    public static async Task<PrintHost> Start(IReadOnlyDictionary<string, byte[]>? files = null, string? chromium = null)
    {
        var webRoot = Directory.CreateTempSubdirectory("rask-pdf-webroot-");
        foreach (var (path, body) in files ?? new Dictionary<string, byte[]>(StringComparer.Ordinal))
        {
            var target = Path.Combine(webRoot.FullName, path.TrimStart('/'));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllBytesAsync(target, body);
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { WebRootPath = webRoot.FullName });
        builder.WebHost.UseTestServer();
        builder.Configuration["Rask:Pdf:ChromiumPath"] = chromium ?? TestChromium.Installed ?? "/nowhere/chromium";
        builder.Configuration["Rask:Pdf:NoSandbox"] = TestChromium.NoSandbox.ToString(CultureInfo.InvariantCulture);
        builder.Services.AddAuthorization();
        builder.Services.AddRask();
        builder.Services.AddRaskPdf();

        var app = builder.Build();
        app.MapRask<PrintTestApp>();
        await app.StartAsync();
        return new PrintHost(app, webRoot);
    }

    /// <summary>Runs <paramref name="print"/> as a handler of <paramref name="user"/>'s session would.</summary>
    public async Task<byte[]> As(ClaimsPrincipal user, Func<Task<byte[]>> print)
    {
        var scope = _app.Services.CreateAsyncScope();
        await using (scope)
        {
            scope.ServiceProvider.GetRequiredService<SessionUserProvider>().Set(user);
            using (Ambient.Enter(scope.ServiceProvider))
            {
                return await print();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        _webRoot.Delete(recursive: true);
    }

    public static ClaimsPrincipal Anonymous { get; } = new(new ClaimsIdentity());

    public static ClaimsPrincipal SignedIn(string name, params string[] roles) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, name), new Claim(ClaimTypes.NameIdentifier, name), .. roles.Select(role => new Claim(ClaimTypes.Role, role))],
        "Test"));
}
