namespace Rask.Cqrs;

/// <summary>Handles a single <see cref="ICommand{TResult}"/> type that returns a value.</summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResult">The result the command returns.</typeparam>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    /// <summary>Executes the command and returns its result. Cancelled with the work that sent it.</summary>
    Task<TResult> Handle(TCommand command);
}
