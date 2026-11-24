using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Rask.Data.Tests;

/// <summary>
///     A model cache that never hands one context's model to the next: for a test whose contexts share a type but
///     declare different models. The ONE such factory in this project, on purpose: every distinct
///     <c>ReplaceService</c> is another EF Core service provider for the whole test process, and past twenty EF Core
///     refuses to build more (<c>ManyServiceProvidersCreatedWarning</c>), failing whichever test happens to be next.
/// </summary>
internal sealed class EachContextItsOwnModel : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => (context, designTime);
}
