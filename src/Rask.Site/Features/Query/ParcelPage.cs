namespace Rask.Site.Features;

public sealed record ParcelPage(int Page, int Pages, IReadOnlyList<Parcel> Rows);
