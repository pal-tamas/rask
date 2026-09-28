using Rask.Cqrs;
using Rask.Querying;

namespace Rask.Site.Features;

// --- Command: once it succeeds, every cached page and parcel is refetched ---
[Invalidates(typeof(GetParcelPage), typeof(GetParcel))]
public sealed record ShipParcel(int Id) : ICommand;
