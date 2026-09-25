using Rask.Cqrs;

namespace Rask.Site.Features;

public sealed class GetParcelHandler(ParcelStore store) : IQueryHandler<GetParcel, Parcel?>
{
    public async Task<Parcel?> Handle(GetParcel query)
    {
        await Task.Delay(300, Current.Cancellation);
        return store.Find(query.Id);
    }
}
