using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Rask.Meta.Hosting.Tests;

/// <summary>
///     That the supervisor actually runs when a real host starts it.
/// </summary>
/// <remarks>
///     The parts were tested and the seam was not. Every other supervisor test drives
///     <see cref="NodeSupervisor.RunAsync" /> directly, and every forwarder test sets
///     <see cref="MetaHostingOptions.SuperviseNode" /> to <c>false</c> — so nothing in the suite
///     established that registering the hosted service causes any of it to happen. That is the gap
///     where a package ships doing nothing at all, with a green suite.
/// </remarks>
public class SupervisorSeamTests
{
    private static WebApplication BuildHost(Action<MetaHostingOptions> configure)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddRaskMeta(configure);
        return builder.Build();
    }

    /// <summary>
    ///     Starting the host runs the supervisor, and forwarding opens.
    /// </summary>
    /// <remarks>
    ///     With supervision off there is no process to wait for, so readiness is the observable proof
    ///     that the hosted service was reached at all.
    /// </remarks>
    [Fact]
    public async Task Starting_the_host_runs_the_supervisor()
    {
        await using var app = BuildHost(options => options.SuperviseNode = false);

        Assert.False(app.Services.GetRequiredService<NodeReadiness>().IsReady);

        await app.StartAsync();

        Assert.True(app.Services.GetRequiredService<NodeReadiness>().IsReady);
    }

    /// <summary>
    ///     A request arriving after the drain begins is refused rather than forwarded.
    /// </summary>
    /// <remarks>
    ///     Closing the door is what makes the wait terminate: without it the in-flight count keeps
    ///     being topped up by new arrivals and the drain sits out its whole budget under any load.
    /// </remarks>
    [Fact]
    public async Task A_request_after_the_drain_begins_is_refused()
    {
        await using var app = BuildHost(options => options.SuperviseNode = false);
        await app.StartAsync();

        app.Services.GetRequiredService<MetaDrain>().BeginDrain();

        var forwarder = app.Services.GetRequiredService<NodeForwarder>();
        var context = new DefaultHttpContext();
        context.Request.Path = "/page";
        context.Response.Body = new MemoryStream();

        await forwarder.ForwardAsync(context);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
    }

    /// <summary>
    ///     A missing server entry fails startup with a message naming the path.
    /// </summary>
    /// <remarks>
    ///     This test is why the check moved into <c>StartAsync</c>. Calling <c>StopApplication()</c>
    ///     from the supervision loop also ended the process, but it did so by cancelling Kestrel's
    ///     <c>BindAsync</c> mid-startup, so the app died with <c>TaskCanceledException</c> — a message
    ///     that says nothing about the front end, on top of the Critical log line that did. An
    ///     unbuilt front end is a configuration mistake, and it should name itself.
    /// </remarks>
    [Fact]
    public async Task A_missing_entry_fails_startup_and_names_the_path()
    {
        await using var app = BuildHost(options =>
        {
            options.AppDirectory = Path.Combine(AppContext.BaseDirectory, "no-such-frontend");
            options.Framework = MetaFramework.Nuxt;
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync());

        Assert.Contains("nuxt", error.Message, StringComparison.Ordinal);
        Assert.Contains("no-such-frontend", error.Message, StringComparison.Ordinal);
        Assert.Contains("AppDirectory", error.Message, StringComparison.Ordinal);
    }
}
