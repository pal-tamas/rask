using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.Data;

/// <summary>
///     What <see cref="LazyLoadingOptionsBuilderExtensions.AllowLazyLoading(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder)" />
///     leaves on a context's options: an interceptor that intercepts nothing, so the guard can see it.
/// </summary>
/// <remarks>
///     An interceptor rather than an options extension: a new extension is a new internal service provider for EF Core,
///     and a test suite (or an app) configuring contexts both ways soon reaches EF's twenty-providers warning — an error in
///     Rask's tests.
/// </remarks>
internal sealed class LazyLoadingAllowed : IInterceptor
{
    internal static readonly LazyLoadingAllowed Instance = new();

    private LazyLoadingAllowed()
    {
    }
}
