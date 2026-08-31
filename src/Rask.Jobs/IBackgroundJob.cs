using Rask.Cqrs;

namespace Rask.Jobs;

/// <summary>
/// A unit of background work. Enqueue it with <see cref="IJob"/>; the
/// <see cref="JobProcessor{TContext}"/> runs it later by dispatching it to its single
/// <see cref="ICommandHandler{TCommand}"/> through <c>Rask.Cqrs</c>. A job <b>is</b> a command — one
/// executed off the request thread, persisted durably, and retried on failure — so you write an ordinary
/// <c>ICommandHandler&lt;TJob&gt;</c> to handle it.
/// </summary>
public interface IBackgroundJob : ICommand;
