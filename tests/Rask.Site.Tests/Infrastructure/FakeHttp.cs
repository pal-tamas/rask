using System.Globalization;
using System.Net;
using System.Text;

namespace Rask.Site.Tests.Infrastructure;

internal sealed class FakeHttp : HttpMessageHandler
{
    public int RequestCount;
    public Func<HttpRequestMessage, Task<HttpResponseMessage>>? Action { get; set; }
    public List<HttpRequestMessage> Requests { get; } = [];

    public static (HttpClient Client, FakeHttp Action) WithPrices(params (string Asset, decimal Price)[] prices)
    {
        var byAsset = prices.ToDictionary(p => p.Asset, p => p.Price, StringComparer.OrdinalIgnoreCase);
        var handler = new FakeHttp
        {
            Action = req =>
            {
                // CoinGecko: /api/v3/simple/price?ids={id}&vs_currencies=usd → {"id":{"usd":N}}.
                // Unknown ids return an empty object (not 404), matching the live API.
                var asset = req.RequestUri!.Query.TrimStart('?').Split('&')
                    .Select(p => p.Split('=', 2))
                    .FirstOrDefault(p => p.Length == 2 && p[0] == "ids")?[1] ?? string.Empty;
                var body = byAsset.TryGetValue(asset, out var price)
                    ? $"{{\"{asset}\":{{\"usd\":{price.ToString(CultureInfo.InvariantCulture)}}}}}"
                    : "{}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            }
        };
        return (new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") }, handler);
    }

    public static (HttpClient Client, FakeHttp Action) WithJson(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new FakeHttp
        {
            Action = _ => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            })
        };
        return (new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") }, handler);
    }

    /// <summary>
    ///     A client that answers from the site's own <c>wwwroot</c> — what a demo fetching a relative URL gets
    ///     from the host that published it — and 404s everything else. Never the network: a lookup of a host
    ///     that does not exist fails through the demos' retry delays, or hangs on a slow resolver, and a page
    ///     rendered in a unit test then waits on DNS instead of settling (#1108).
    /// </summary>
    public static HttpClient ServingSiteFiles()
    {
        var root = Path.GetFullPath(RepoPaths.SiteWebRoot) + Path.DirectorySeparatorChar;
        var handler = new FakeHttp
        {
            Action = req =>
            {
                var relative = Uri.UnescapeDataString(req.RequestUri!.AbsolutePath).TrimStart('/');
                var file = Path.GetFullPath(Path.Combine(root, relative));
                if (!file.StartsWith(root, StringComparison.Ordinal) || !File.Exists(file))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                }

                var content = new ByteArrayContent(File.ReadAllBytes(file));
                content.Headers.ContentType = new(Path.GetExtension(file) == ".json" ? "application/json" : "application/octet-stream");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            }
        };
        return new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
    }

    public static (HttpClient Client, FakeHttp Action) WithStatus(HttpStatusCode status)
    {
        var handler = new FakeHttp { Action = _ => Task.FromResult(new HttpResponseMessage(status)) };
        return (new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") }, handler);
    }

    public static (HttpClient Client, FakeHttp Action) Throwing(Exception ex)
    {
        var handler = new FakeHttp { Action = _ => throw ex };
        return (new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") }, handler);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref RequestCount);
        lock (Requests)
        {
            Requests.Add(request);
        }

        return Action is null
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))
            : Action(request);
    }
}
