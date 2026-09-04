using System.Net;

namespace Rask.Examples.E2E.Tests.Infrastructure;

/// <summary>
///     Waits for the supervised Node front end, which is a second readiness gate behind Kestrel's.
/// </summary>
/// <remarks>
///     <para>
///         The base fixture waits for the HOST to answer. On this lane that is not the same as the app
///         being up: Kestrel binds first and supervises node afterwards, and until the child is
///         listening every forwarded request is answered <c>503</c> with a <c>Retry-After</c> — by
///         design, and better than a 502 from forwarding into a closed socket.
///     </para>
///     <para>
///         That window is short when a sample runs alone and long when four of them boot at once, each
///         with a node server of its own. Journeys were failing on the 503 rather than on anything they
///         were written to check, which is a test asserting the wrong thing: the lane's own contract
///         says those seconds are normal.
///     </para>
/// </remarks>
internal static class MetaFrontEnd
{
    /// <summary>How long to allow the node child to start. Generous because the gate runs six of these.</summary>
    private static readonly TimeSpan Budget = TimeSpan.FromMinutes(2);

    /// <summary>Polls the app's root until the front end answers, and returns that response's body.</summary>
    public static async Task<string> WaitForPageAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var deadline = DateTime.UtcNow + Budget;
        var last = HttpStatusCode.ServiceUnavailable;

        while (DateTime.UtcNow < deadline)
        {
            var response = await http.GetAsync("/");
            last = response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }

            // Anything but the documented startup window is a real failure and should not be waited out.
            if (last != HttpStatusCode.ServiceUnavailable)
            {
                Assert.Fail($"{baseUrl} answered {(int)last} {last} rather than a page.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        Assert.Fail($"{baseUrl} never left {(int)last} {last} within {Budget.TotalSeconds:0}s.");
        return string.Empty;
    }
}
