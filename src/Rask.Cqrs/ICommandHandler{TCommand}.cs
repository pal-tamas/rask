namespace Rask.Cqrs;

/// <summary>Handles a single void <see cref="ICommand"/> type.</summary>
/// <typeparam name="TCommand">The command type.</typeparam>
public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    /// <summary>Executes the command. Cancelled with the work that sent it — read <c>Current.Cancellation</c> to pass on.</summary>
    Task Handle(TCommand command);
}
