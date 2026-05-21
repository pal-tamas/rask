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
                var asset = req.RequestUri!.AbsolutePath.Split('/').Last();
                if (!byAsset.TryGetValue(asset, out var price))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                }

                var body = $"{{\"data\":{{\"priceUsd\":\"{price.ToString(CultureInfo.InvariantCulture)}\",\"symbol\":\"{asset}\"}}}}";
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
