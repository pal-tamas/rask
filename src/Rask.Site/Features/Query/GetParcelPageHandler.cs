using Rask.Cqrs;

namespace Rask.Site.Features;

public sealed class GetParcelPageHandler(ParcelStore store) : IQueryHandler<GetParcelPage, ParcelPage>
{
    public async Task<ParcelPage> Handle(GetParcelPage query)
    {
        await Task.Delay(500, Current.Cancellation);
        return new ParcelPage(query.Page, store.Pages, store.Page(query.Page));
    }
}
