using System.Reflection;
using Rask.Tests.Shared;

namespace Rask.Auth.Tests;

/// <summary>
///     Every test class here builds an <see cref="AuthDbContext" />, so every one must be in the
///     collection that serialises them. This fires when the shape comes back, rather than waiting for the
///     model-cache race to surface on the full-solution gate.
/// </summary>
public sealed class AuthDbCollectionGuardTests
{
    [Fact]
    public void Every_test_class_that_touches_the_database_is_collected() =>
        DbCollectionGuard.AssertEveryTestClassIsCollected(
            Assembly.GetExecutingAssembly(),
            AuthDbCollection.Name,
            // This class only reflects over the assembly; it never builds a context.
            nameof(AuthDbCollectionGuardTests));
}
