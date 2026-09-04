using Microsoft.AspNetCore.Builder;
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
[Collection(MetaHostCollection.Name)]
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
