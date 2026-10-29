using Rask.Cqrs;

namespace Rask.Background;

/// <summary>
/// A unit of background work. Enqueue it with <c>Jobs.Enqueue(job)</c>; the
/// <see cref="JobProcessor{TContext}"/> runs it later by handing it to its single
/// <see cref="IJobHandler{TJob}"/> — off the request thread, persisted durably, and retried on failure.
/// Underneath it travels through <c>Rask.Cqrs</c> as an <see cref="ICommand"/>, which is why a handler written
/// as <c>ICommandHandler&lt;TJob&gt;</c> also runs it.
/// </summary>
[LocalOnly]
public interface IJob : ICommand;
