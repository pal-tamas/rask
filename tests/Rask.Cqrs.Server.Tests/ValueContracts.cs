using Rask.Values;

namespace Rask.Cqrs.Server.Tests;

public sealed partial record DriverName(string Value) : Text(Value, MaxLength: 10);

public sealed record Driver(DriverName Name);

public sealed record RenameDriver(int Id, Driver Driver) : ICommand;

public sealed class RenameDriverHandler : ICommandHandler<RenameDriver>
{
    public static DriverName? Received { get; set; }

    public Task Handle(RenameDriver command)
    {
        Received = command.Driver.Name;
        return Task.CompletedTask;
    }
}
