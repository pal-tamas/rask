using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Diagnostics;
using Rask.Server.Tests.Infrastructure;
using Rask.Server.Transport;

namespace Rask.Server.Tests.Authentication;

/// <summary>
///     A revalidator asked while its client goes away ends with the client, however it says so (#1256).
/// </summary>
/// <remarks>
///     EF's SQL Server retrying strategy reports a cancelled query as an <see cref="InvalidOperationException" />, not
///     an <see cref="OperationCanceledException" />. Taken at its word, an aborted hello was an unhandled exception and
///     an aborted event an error line. A revalidator that fails while nobody cancelled it still refuses.
/// </remarks>
[Collection(RaskDiagnosticsSinkCollection.Name)]
public sealed class CancelledRevalidationTests
{
    [Fact]
    public async Task A_hello_whose_revalidation_is_cancelled_ends_as_a_cancellation_and_attaches_nothing()
    {
        var revalidator = new EfLikeRevalidator(failsAtOnce: false);
        using var host = CreateHost(revalidator);
        var session = await SignedInSessionAsync(host);
        using var aborted = new CancellationTokenSource();
        var errors = new ConcurrentQueue<RaskDiagnosticEvent>();

        var thrown = await CapturingErrorsAsync(errors, async () =>
        {
            var hello = HelloAsync(host, session, aborted.Token);
            await revalidator.Asked.Task.WaitAsync(TestContext.Current.CancellationToken);
            await aborted.CancelAsync();
            return await Record.ExceptionAsync(() => hello);
        });

        var cancelled = Assert.IsAssignableFrom<OperationCanceledException>(thrown);
        Assert.IsType<InvalidOperationException>(cancelled.InnerException);
        Assert.Empty(errors);
        Assert.False(session.HasOpenTransport);
        Assert.Equal(0, host.Store.ConnectedCount);
    }

    [Fact]
    public async Task A_hello_whose_revalidator_fails_uncancelled_is_still_refused()
    {
        using var host = CreateHost(new EfLikeRevalidator(failsAtOnce: true));
        var session = await SignedInSessionAsync(host);

        var thrown = await Record.ExceptionAsync(() => HelloAsync(host, session, CancellationToken.None));

        Assert.IsType<InvalidOperationException>(thrown);
        Assert.False(session.HasOpenTransport);
        Assert.Equal(0, host.Store.ConnectedCount);
    }

    [Fact]
    public async Task An_event_whose_revalidation_is_cancelled_reports_no_error()
    {
        var revalidator = new EfLikeRevalidator(failsAtOnce: false);
        using var host = CreateHost(revalidator);
        var session = await SignedInSessionAsync(host);
        using var aborted = new CancellationTokenSource();
        var errors = new ConcurrentQueue<RaskDiagnosticEvent>();

        var thrown = await CapturingErrorsAsync(errors, async () =>
        {
            var asked = RaskEndpointExtensions.TryAskWhoAsync(session, "the event", aborted.Token).AsTask();
            await revalidator.Asked.Task.WaitAsync(TestContext.Current.CancellationToken);
            await aborted.CancelAsync();
            return await Record.ExceptionAsync(() => asked);
        });

        Assert.IsAssignableFrom<OperationCanceledException>(thrown);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task An_event_whose_revalidator_fails_uncancelled_is_refused_and_reported()
    {
        using var host = CreateHost(new EfLikeRevalidator(failsAtOnce: true));
        var session = await SignedInSessionAsync(host);
        var errors = new ConcurrentQueue<RaskDiagnosticEvent>();

        var ran = await CapturingErrorsAsync(errors, async () =>
            await RaskEndpointExtensions.TryAskWhoAsync(session, "the event", CancellationToken.None));

        Assert.False(ran);
        Assert.Single(errors);
    }

    private static RaskTestHost CreateHost(EfLikeRevalidator revalidator) =>
        RaskTestHost.Create<TestApp>(services => services.AddSingleton<ISessionRevalidator>(revalidator));

    private static readonly ClaimsPrincipal Alice = new(new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, "alice"), new Claim(ClaimTypes.NameIdentifier, "alice")], "Test"));

    private static async Task<LiveSession> SignedInSessionAsync(RaskTestHost host)
    {
        var html = await host.Http.GetStringAsync("/start", TestContext.Current.CancellationToken);
        var session = host.Store.Peek(MarkupAssert.SessionId(html))!;
        session.Users.Set(Alice);
        return session;
    }

    private static Task<RaskEndpointExtensions.AttachOutcome> HelloAsync(
        RaskTestHost host, LiveSession session, CancellationToken ct) =>
        RaskEndpointExtensions.AttachAsync(
            session.Id, resumeToken: null, sentBefore: 0, new Connection(), host.Store,
            host.Services.GetRequiredService<RaskServerLimits>(), Alice,
            host.Services.GetRequiredService<SessionResumeSupport>(),
            // Only a resume rebuild consults it; this session is known.
            new RaskRootSelector(_ => new Shell(), []),
            metrics: null, resumeCulture: default, ct);

    // After the host is up: mapping it points the sink at its own logger.
    private static async Task<T> CapturingErrorsAsync<T>(ConcurrentQueue<RaskDiagnosticEvent> errors, Func<Task<T>> act)
    {
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = e =>
        {
            if (e.Level == RaskLogLevel.Error)
            {
                errors.Enqueue(e);
            }
        };
        try
        {
            return await act();
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }
    }

    /// <summary>Reads the user's row as EF over SQL Server does: a cancelled query comes out as an InvalidOperationException.</summary>
    private sealed class EfLikeRevalidator(bool failsAtOnce) : ISessionRevalidator
    {
        public TaskCompletionSource Asked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<ClaimsPrincipal?> Revalidate(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
        {
            Asked.TrySetResult();
            if (failsAtOnce)
            {
                await Task.Yield();
                throw new InvalidOperationException("The database is unreachable.");
            }

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException cancelled)
            {
                throw new InvalidOperationException(
                    "An exception has been raised that is likely due to a transient failure.", cancelled);
            }

            return principal;
        }
    }

    private sealed class Shell : Component
    {
        protected override Component? Render() => null;
    }

    private sealed class Connection : ILiveTransport
    {
        public bool IsOpen { get; private set; } = true;

        public ConnectionRecord Record { get; } = new(0, "test", 0);

        public ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) => ValueTask.CompletedTask;

        public Task CloseAsync(LiveTransportClose reason, string description, CancellationToken ct)
        {
            IsOpen = false;
            return Task.CompletedTask;
        }

        public void Abort() => IsOpen = false;
    }
}
