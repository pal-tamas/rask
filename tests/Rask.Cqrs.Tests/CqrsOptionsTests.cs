using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.Cqrs.Tests;

// Configuration-validation coverage for CqrsOptions: the AddOpenBehavior guard rails, the Validate() enum checks
// that AddRaskCqrs runs at registration time, and the Rask:Cqrs section it reads while it registers.
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

    // Rask:Cqrs is read while services are registered, because the handler lifetime and the validation switch decide
    // which descriptors exist. The validation behavior is registered at the handler lifetime, which makes it the
    // descriptor to read both from.
    [Fact]
    public void Rask_Cqrs_sets_the_lifetime_the_pipeline_is_registered_at()
    {
        var services = Host(new() { ["Rask:Cqrs:HandlerLifetime"] = "Scoped" });

        services.AddRaskCqrs();

        Assert.Equal(ServiceLifetime.Scoped, ValidationBehavior(services).Lifetime);
    }

    [Fact]
    public void Rask_Cqrs_can_switch_request_validation_off()
    {
        var services = Host(new() { ["Rask:Cqrs:ValidateRequests"] = "false" });

        services.AddRaskCqrs();

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IPipelineBehavior<,>));
    }

    [Fact]
    public void The_callback_wins_over_Rask_Cqrs()
    {
        var services = Host(new() { ["Rask:Cqrs:HandlerLifetime"] = "Scoped" });

        services.AddRaskCqrs(o => o.HandlerLifetime = ServiceLifetime.Singleton);

        Assert.Equal(ServiceLifetime.Singleton, ValidationBehavior(services).Lifetime);
    }

    [Theory]
    [InlineData("Forever")]
    [InlineData("7")]
    public void A_value_that_names_nothing_is_refused_naming_the_key(string value)
    {
        var services = Host(new() { ["Rask:Cqrs:HandlerLifetime"] = value });

        var error = Assert.Throws<InvalidOperationException>(() => services.AddRaskCqrs());

        Assert.Contains("Rask:Cqrs:HandlerLifetime", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_container_that_is_not_a_host_reads_no_configuration()
    {
        // An IConfiguration registered as a service is not readable while registering — only the host builder's
        // context is — so a bare container keeps the defaults.
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(Configuration(new() { ["Rask:Cqrs:HandlerLifetime"] = "Scoped" }));

        services.AddRaskCqrs();

        Assert.Equal(ServiceLifetime.Transient, ValidationBehavior(services).Lifetime);
    }

    private static ServiceDescriptor ValidationBehavior(IServiceCollection services) =>
        services.Single(d => d.ServiceType == typeof(IPipelineBehavior<,>));

    // What HostApplicationBuilder and WebApplicationBuilder put in the collection before any AddX runs.
    private static ServiceCollection Host(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new HostBuilderContext(new Dictionary<object, object>())
        {
            Configuration = Configuration(settings),
        });
        return services;
    }

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
