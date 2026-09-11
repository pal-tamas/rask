using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Rask.Signaling.Tests;

// RaskSignalingOptions come from Rask:Signaling first and the AddRaskSignaling callback second. Validation runs
// when the options are built — at host start, or the first resolve on a bare container — not at registration.
public class SignalingOptionsBindingTests
{
    [Fact]
    public void TheRaskSignalingSection_SetsTheOptions()
    {
        using var provider = Provider(new()
        {
            ["Rask:Signaling:Path"] = "/rtc",
            ["Rask:Signaling:MaxPeersPerRoom"] = "4",
            ["Rask:Signaling:RequireAuthorization"] = "false",
        });

        var options = provider.GetRequiredService<RaskSignalingOptions>();

        Assert.Equal("/rtc", options.Path);
        Assert.Equal(4, options.MaxPeersPerRoom);
        Assert.False(options.RequireAuthorization);
    }

    [Fact]
    public void TheCallback_WinsOverConfiguration()
    {
        using var provider = Provider(
            new() { ["Rask:Signaling:MaxPeersPerRoom"] = "4" },
            o => o.MaxPeersPerRoom = 6);

        Assert.Equal(6, provider.GetRequiredService<RaskSignalingOptions>().MaxPeersPerRoom);
    }

    [Fact]
    public void ConfigurationFillsWhatTheCallbackLeavesAlone()
    {
        using var provider = Provider(
            new() { ["Rask:Signaling:MaxRooms"] = "50" },
            o => o.MaxPeersPerRoom = 6);

        var options = provider.GetRequiredService<RaskSignalingOptions>();

        Assert.Equal(50, options.MaxRooms);
        Assert.Equal(6, options.MaxPeersPerRoom);
    }

    [Fact]
    public void WithoutConfiguration_TheDefaultsApply()
    {
        var services = new ServiceCollection();
        services.AddRaskSignaling();
        using var provider = services.BuildServiceProvider();

        Assert.Equal(8, provider.GetRequiredService<RaskSignalingOptions>().MaxPeersPerRoom);
    }

    [Fact]
    public void ATopLevelSignalingSection_IsNotRead()
    {
        using var provider = Provider(new() { ["Signaling:MaxPeersPerRoom"] = "4" });

        Assert.Equal(8, provider.GetRequiredService<RaskSignalingOptions>().MaxPeersPerRoom);
    }

    [Fact]
    public void ASecondRegistration_KeepsTheFirstOptions()
    {
        var services = new ServiceCollection();
        services.AddRaskSignaling(o => o.MaxRooms = 10);
        services.AddRaskSignaling(o => o.MaxRooms = 20);
        using var provider = services.BuildServiceProvider();

        Assert.Equal(10, provider.GetRequiredService<RaskSignalingOptions>().MaxRooms);
        Assert.Single(services, d => d.ServiceType == typeof(SignalingHub));
    }

    [Theory]
    [InlineData("no-leading-slash")]
    public void APathThatIsNotRooted_IsRejected(string path)
    {
        using var provider = Provider(new(), o => o.Path = path);

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<RaskSignalingOptions>());
        Assert.Contains("Rask:Signaling", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APayloadCapAboveTheMessageCap_IsRejected()
    {
        using var provider = Provider(new(), o =>
        {
            o.MaxMessageBytes = 2048;
            o.MaxPayloadBytes = 4096;
        });

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<RaskSignalingOptions>());
        Assert.Contains("MaxPayloadBytes", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOutOfRangeConfiguredValue_IsRejectedNamingTheSection()
    {
        using var provider = Provider(new() { ["Rask:Signaling:MaxPeersPerRoom"] = "1" });

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<RaskSignalingOptions>());
        Assert.Contains("Rask:Signaling", ex.Message, StringComparison.Ordinal);
        Assert.Contains("MaxPeersPerRoom", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AValueThatIsNotANumber_IsRejectedNamingTheSection()
    {
        using var provider = Provider(new() { ["Rask:Signaling:MaxRooms"] = "lots" });

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<RaskSignalingOptions>());
        Assert.Contains("Rask:Signaling", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInvalidConfiguredValue_StopsTheHostStarting()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Rask:Signaling:MaxPeersPerRoom"] = "1",
        });
        builder.Services.AddRaskSignaling();
        await using var app = builder.Build();

        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() => app.StartAsync());
        Assert.Contains("Rask:Signaling", ex.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider Provider(
        Dictionary<string, string?> settings, Action<RaskSignalingOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddRaskSignaling(configure);
        return services.BuildServiceProvider();
    }
}
