namespace Rask.TypeScript.Tasks.Tests;

/// <summary>
///     A fetch that fails for a moment is asked again; one that will fail the same way every time is not.
/// </summary>
public class TransientRetryTests
{
    private static readonly IReadOnlyList<TimeSpan> TwoWaits = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)];

    [Fact]
    public void A_fetch_that_drops_twice_is_asked_again_and_the_third_answer_is_used()
    {
        var attempts = 0;
        var told = new List<int>();
        var slept = new List<TimeSpan>();

        var answer = TransientRetry.Run(
            () => ++attempts < 3 ? throw new HttpRequestException("Connection reset by peer") : "tarball",
            TwoWaits,
            (_, next) => told.Add(next),
            slept.Add);

        Assert.Equal("tarball", answer);
        Assert.Equal(3, attempts);
        Assert.Equal([2, 3], told);
        Assert.Equal(TwoWaits, slept);
    }

    [Fact]
    public void A_package_the_registry_does_not_have_fails_at_once()
    {
        var attempts = 0;

        var failure = Record.Exception(() => TransientRetry.Run<string>(
            () =>
            {
                attempts++;
                throw new RegistryAnswerException(404, "https://registry.npmjs.org/x.tgz");
            },
            TwoWaits,
            (_, _) => { },
            _ => { }));

        Assert.IsType<RegistryAnswerException>(failure);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void A_network_that_never_comes_back_gives_up_after_the_last_wait_with_its_own_failure()
    {
        var attempts = 0;

        var failure = Record.Exception(() => TransientRetry.Run<string>(
            () =>
            {
                attempts++;
                throw new HttpRequestException("Name or service not known");
            },
            TwoWaits,
            (_, _) => { },
            _ => { }));

        Assert.Equal("Name or service not known", failure?.Message);
        Assert.Equal(3, attempts);
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(502, true)]
    [InlineData(503, true)]
    [InlineData(408, true)]
    [InlineData(429, true)]
    [InlineData(404, false)]
    [InlineData(403, false)]
    public void A_registry_answer_is_asked_again_only_when_it_could_change(int status, bool asksAgain)
    {
        var failure = new RegistryAnswerException(status, "https://registry.npmjs.org/x.tgz");

        var transient = TransientRetry.IsTransient(failure);

        Assert.Equal(asksAgain, transient);
    }

    [Fact]
    public void A_timeout_and_a_broken_download_are_asked_again_but_a_download_that_is_not_the_package_is_not()
    {
        var timeout = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout");
        var broken = new IOException("Unable to read data from the transport connection", new IOException("reset"));
        var wrongBytes = new IOException("the download is not the package that was expected");

        var asked = (TransientRetry.IsTransient(timeout), TransientRetry.IsTransient(broken), TransientRetry.IsTransient(wrongBytes));

        Assert.Equal((true, true, false), asked);
    }
}
