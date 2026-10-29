using System.Text;

namespace Rask.Cqrs.Server.Tests;

/// <summary>
///     A command once sent runs to its end though its caller has gone — a tab closed mid-save aborts the fetch, not the
///     write — while a query, which changes nothing, is still the caller's to abandon.
/// </summary>
public sealed class AbandonedRequestTests
{
    [Fact]
    public async Task A_command_whose_caller_went_away_runs_to_its_end()
    {
        using var server = EndpointSecurityTests.Host();
        using var client = server.CreateClient();
        using var request = EndpointSecurityTests.Request(HttpMethod.Post, "Rask.Cqrs.Server.Tests.SaveSlowly", authenticated: true);
        request.Content = new StringContent("""{"id":3}""", Encoding.UTF8, "application/json");
        using var tab = new CancellationTokenSource();
        var sent = client.SendAsync(request, tab.Token);
        await SaveSlowlyHandler.Reached.Task.WaitAsync(TestContext.Current.CancellationToken);

        await tab.CancelAsync();
        SaveSlowlyHandler.Gate.SetResult();

        Assert.Equal(3, await SaveSlowlyHandler.Saved.Task.WaitAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sent);
    }
}
