using Rask.Cqrs;
using Rask.Querying;

namespace Rask.Site.Features;

public sealed record GetParcel(int Id) : IQuery<Parcel?>;
