using Rask.Cqrs;

namespace Rask.Site.Features;

public sealed class ShipParcelHandler(ParcelStore store) : ICommandHandler<ShipParcel>
{
    public async Task Handle(ShipParcel command)
    {
        await Task.Delay(600, Current.Cancellation);
        store.Ship(command.Id);
    }
}
