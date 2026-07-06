using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs.Tests;

// Configuration-validation coverage for CqrsOptions: the AddOpenBehavior guard rails and the
// Validate() enum checks that AddRaskCqrs runs at registration time.
public class CqrsOptionsTests
{
    [Fact]
    public void AddOpenBehavior_rejects_null() =>
        Assert.Throws<ArgumentNullException>(() => new CqrsOptions().AddOpenBehavior(null!));

    [Fact]
    public void AddOpenBehavior_rejects_a_non_generic_type() =>
        Assert.Throws<ArgumentException>(() => new CqrsOptions().AddOpenBehavior(typeof(string)));

    [Fact]
    public void AddOpenBehavior_rejects_a_generic_with_wrong_arity() =>
        // List<> is an open generic but has ONE type parameter, so it fails the two-parameter check.
        Assert.Throws<ArgumentException>(() => new CqrsOptions().AddOpenBehavior(typeof(List<>)));

    [Fact]
    public void AddOpenBehavior_rejects_a_two_param_generic_that_is_not_a_behavior() =>
        // Correct arity (two type params) but does not implement IPipelineBehavior<,> — the second guard.
        Assert.Throws<ArgumentException>(() => new CqrsOptions().AddOpenBehavior(typeof(Dictionary<,>)));

    [Fact]
    public void AddRaskCqrs_rejects_an_invalid_handler_lifetime() =>
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddRaskCqrs(o => o.HandlerLifetime = (ServiceLifetime)99));

    [Fact]
    public void AddRaskCqrs_rejects_an_invalid_publish_strategy() =>
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddRaskCqrs(o => o.NotificationPublishStrategy = (NotificationPublishStrategy)99));
}
