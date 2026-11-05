namespace Rask.Background;

/// <summary>The timing steps on an injected <see cref="IJobs" />, worded as on <see cref="Jobs" />.</summary>
public static class JobsExtensions
{
    extension(IJobs jobs)
    {
        /// <summary>Runs <paramref name="job" /> in the background, as soon as the processor next polls.</summary>
        public Enqueuing Enqueue(IJob job, CancellationToken cancellationToken = default) =>
#pragma warning disable CA2208 // CA2208 cannot see a C# 14 extension receiver
            new(jobs ?? throw new ArgumentNullException(nameof(jobs)), job, null, null, null, cancellationToken);
#pragma warning restore CA2208

        /// <summary>Runs <paramref name="job" /> as a job and waits for its answer, as <c>Jobs.Run(job)</c> does.</summary>
        public Running<TResult> Run<TResult>(IJob<TResult> job, CancellationToken cancellationToken = default) =>
#pragma warning disable CA2208 // CA2208 cannot see a C# 14 extension receiver
            new(jobs ?? throw new ArgumentNullException(nameof(jobs)), job, null, null, cancellationToken);
#pragma warning restore CA2208

        /// <summary>Runs <paramref name="job" /> as a job and waits until it has run, as <c>Jobs.Run(job)</c> does.</summary>
        public Running Run(IJob job, CancellationToken cancellationToken = default) =>
#pragma warning disable CA2208 // CA2208 cannot see a C# 14 extension receiver
            new(jobs ?? throw new ArgumentNullException(nameof(jobs)), job, null, null, cancellationToken);
#pragma warning restore CA2208
    }
}
