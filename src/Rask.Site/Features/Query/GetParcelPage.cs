using Rask.Cqrs;
using Rask.Querying;

namespace Rask.Site.Features;

// --- Queries: the message is the cache key, so GetParcelPage(2) and GetParcelPage(3) are cached apart ---
public sealed record GetParcelPage(int Page) : IQuery<ParcelPage>;
