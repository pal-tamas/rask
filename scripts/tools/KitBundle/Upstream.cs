using System.Net;
using System.Text.RegularExpressions;

namespace KitBundle;

/// <summary>What an upstream answers, over HTTP — or, under <c>RASK_UPSTREAM_FIXTURES</c>, from saved answers.</summary>
/// <remarks>
///     The saved answers are one file per URL, named the way <see cref="FixtureName" /> names them (the rule every
///     follower shares, scripts/upstream/upstream.py's), so a test drives the tool with no network. A URL with no
///     saved answer is an upstream that did not answer.
/// </remarks>
internal static partial class Upstream
{
    private static readonly HttpClient Http = CreateClient();

    private static readonly string? Saved = Environment.GetEnvironmentVariable("RASK_UPSTREAM_FIXTURES");

    /// <summary>The file a saved answer for <paramref name="url" /> is kept in.</summary>
    internal static string FixtureName(string url) => NotSafe().Replace(Scheme().Replace(url, string.Empty), "_");

    /// <summary>What <paramref name="url" /> answers, as bytes.</summary>
    /// <exception cref="UnansweredException">It did not answer, or answered that it is busy or broken.</exception>
    /// <exception cref="KitBundleException">It answered with a refusal that will not change tomorrow.</exception>
    internal static async Task<byte[]> Get(string url, string? accept = null)
    {
        if (Saved is not null)
        {
            var path = Path.Combine(Saved, FixtureName(url));
            return File.Exists(path) ? await File.ReadAllBytesAsync(path) : throw new UnansweredException(url, "no saved answer");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (accept is not null)
        {
            request.Headers.Accept.ParseAdd(accept);
        }

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new UnansweredException(url, ex.Message);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            {
                throw new UnansweredException(url, $"HTTP {(int)response.StatusCode}");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new KitBundleException($"{url}: HTTP {(int)response.StatusCode}");
            }

            return await response.Content.ReadAsByteArrayAsync();
        }
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("rask-upstream");
        return http;
    }

    [GeneratedRegex("^[^:]*://")]
    private static partial Regex Scheme();

    [GeneratedRegex("[^A-Za-z0-9.-]+")]
    private static partial Regex NotSafe();
}

/// <summary>An upstream that did not answer: tomorrow's run, so a follower leaves every file as it is.</summary>
internal sealed class UnansweredException(string url, string why) : Exception($"{url} did not answer ({why})");

/// <summary>A failure the run reports and exits non-zero on.</summary>
internal sealed class KitBundleException(string message) : Exception(message);
