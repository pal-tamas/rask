using System.Globalization;
using System.Net;
using System.Text;

namespace Rask.Example.Shared.Tests.Infrastructure;

internal sealed class FakeHttp : HttpMessageHandler
{
    public Func<HttpRequestMessage, Task<HttpResponseMessage>>? Handler { get; set; }
    public int RequestCount;
    public List<HttpRequestMessage> Requests { get; } = [];

    public static (HttpClient Client, FakeHttp Handler) WithPrices(params (string Asset, decimal Price)[] prices)
    {
        var byAsset = prices.ToDictionary(p => p.Asset, p => p.Price, StringComparer.OrdinalIgnoreCase);
        var handler = new FakeHttp
        {
            Handler = req =>
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

    public static (HttpClient Client, FakeHttp Handler) WithJson(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new FakeHttp
        {
            Handler = _ => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            })
        };
        return (new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") }, handler);
    }

    public static (HttpClient Client, FakeHttp Handler) WithStatus(HttpStatusCode status)
    {
        var handler = new FakeHttp { Handler = _ => Task.FromResult(new HttpResponseMessage(status)) };
        return (new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") }, handler);
    }

    public static (HttpClient Client, FakeHttp Handler) Throwing(Exception ex)
    {
        var handler = new FakeHttp { Handler = _ => throw ex };
        return (new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") }, handler);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref RequestCount);
        lock (Requests)
        {
            Requests.Add(request);
        }

        return Handler is null
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))
            : Handler(request);
    }
}
