using Rask.Cqrs;

namespace Rask.Site.Features;

public sealed class ShipParcelHandler(ParcelStore store) : ICommandHandler<ShipParcel>
{
    public async Task Handle(ShipParcel command)
    {
        // Long enough to SEE the row's pending state: the button is disabled from the handler budget (150 ms) on, and a
        // browser test on a busy runner missed a 450 ms window.
        await Task.Delay(1500, Current.Cancellation);
        store.Ship(command.Id);
    }
}
