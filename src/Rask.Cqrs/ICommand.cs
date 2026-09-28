namespace Rask.Cqrs;

/// <summary>
/// Marks a request that performs a side effect and returns no value. Handled by a single
/// <see cref="ICommandHandler{TCommand}"/>. Dispatch it through <see cref="IDispatcher.Send(ICommand, System.Threading.CancellationToken)"/>.
/// </summary>
public interface ICommand;
