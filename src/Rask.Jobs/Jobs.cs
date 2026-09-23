using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Jobs;

/// <summary>
///     The app's job queue, with nothing injected — from a handler, a render, a request, another job:
/// </summary>
/// <remarks>
///     <code>
///     await Jobs.Enqueue(new SendWelcome(user.Id));
///     await Jobs.Enqueue(new ChaseInvoice(id)).In(24.Hours);
///     await Jobs.Enqueue(new CloseBooks()).At(monthEnd);
///     </code>
///     <para>
///         Each call reaches the <see cref="IJobs" /> of the work it runs in and is cancelled with that work.
///         Outside any — a hosted service, a timer started at boot — it throws; inject <see cref="IJobs" />
///         there instead.
///     </para>
/// </remarks>
public static class Jobs
{
    /// <summary>Runs <paramref name="job" /> in the background, as soon as the processor next polls.</summary>
    public static Enqueuing Enqueue(IJob job, CancellationToken cancellationToken = default) =>
        new(null, job, null, null, cancellationToken);

    /// <summary>
    ///     Whether Rask.Jobs is registered at all. For an operator surface that renders "off" rather than
    ///     failing — <c>Rask.Dashboard</c> does exactly that. An app should not branch on this: a call with
    ///     nothing registered throws and names the registration that fixes it.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static bool IsOn =>
        Faked.Value is not null || Ambient.Services?.GetService<IJobs>() is not null;

    /// <summary>What <c>Jobs.Fake()</c> put in the way of the real queue, for this test's flow alone.</summary>
    internal static readonly AsyncLocal<IJobs?> Faked = new();

    internal static IJobs Resolve()
    {
        if (Faked.Value is { } fake)
        {
            return fake;
        }

        var services = Ambient.Services
            ?? throw new InvalidOperationException(
                "Jobs was called outside any work in progress — a handler, a render, a request or a job — so "
                + "there is no app to reach. Inject IJobs in the constructor there instead.");

        return services.GetService<IJobs>()
            ?? throw new InvalidOperationException(
                "Jobs needs Rask.Jobs registered: call builder.Services.AddRaskJobs<AppDbContext>().");
    }
}

/// <summary>The timing steps on an injected <see cref="IJobs" />, worded as on <see cref="Jobs" />.</summary>
public static class JobsExtensions
{
    extension(IJobs jobs)
    {
        /// <summary>Runs <paramref name="job" /> in the background, as soon as the processor next polls.</summary>
        public Enqueuing Enqueue(IJob job, CancellationToken cancellationToken = default) =>
            new(jobs ?? throw new ArgumentNullException(nameof(jobs)), job, null, null, cancellationToken);
    }
}
