using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Client.Browser;
using Rask.Core.Browser;

namespace Rask.Native.Tests;

/// <summary>
///     What a head advertises is <b>derived</b> from what its platform module registers.
/// </summary>
/// <remarks>
///     The alternative designs both fail in ways this pins against. Asking the live container would
///     advertise everything, because the framework registers a JS-backed default for every one of these
///     interfaces and a module uses TryAdd — so "is it registered" cannot tell a native backend from the
///     WebView's own JS. A hand-written list beside <c>Register</c> would answer that, and would drift the
///     first time a sixteenth backend was added and the other half forgotten, which is exactly the failure
///     G1 describes.
/// </remarks>
public class NativeCapabilityRegistryTests
{
    [Fact]
    public void A_module_advertises_every_backend_it_registers()
    {
        var advertised = NativeCapabilityRegistry.AdvertisedFor(new ThreeBackendPlatform());

        Assert.Equal(["share", "clipboard", "badge"], advertised);
    }

    /// <summary>
    ///     The property that makes the derivation worth having: a backend added to <c>Register</c> shows up
    ///     with nothing else to edit. This is the sixteenth-backend case from G1, in miniature.
    /// </summary>
    [Fact]
    public void A_backend_added_to_the_module_advertises_itself()
    {
        var before = NativeCapabilityRegistry.AdvertisedFor(new ThreeBackendPlatform());
        var after = NativeCapabilityRegistry.AdvertisedFor(new FourBackendPlatform());

        Assert.DoesNotContain("vibration", before);
        Assert.Contains("vibration", after);
    }

    [Fact]
    public void A_module_that_registers_nothing_advertises_nothing() =>
        Assert.Empty(NativeCapabilityRegistry.AdvertisedFor(new EmptyPlatform()));

    /// <summary>
    ///     A module may register its own plumbing; only the <c>I</c>-prefixed browser interfaces are things a
    ///     page can invoke, so a concrete helper class must not be advertised as a capability.
    /// </summary>
    [Fact]
    public void Only_interfaces_are_advertised() =>
        Assert.Equal(["share"], NativeCapabilityRegistry.AdvertisedFor(new PlatformWithHelper()));

    [Theory]
    [InlineData(typeof(IShare), "share")]
    [InlineData(typeof(IGeolocation), "geolocation")]
    [InlineData(typeof(IDeviceOrientation), "deviceOrientation")]
    [InlineData(typeof(INetworkInfo), "networkInfo")]
    public void The_wire_name_is_the_interface_without_its_I(Type service, string expected) =>
        Assert.Equal(expected, NativeCapabilityRegistry.CapabilityName(service));

    [Fact]
    public void A_type_that_is_not_an_interface_name_has_no_capability_name() =>
        Assert.Null(NativeCapabilityRegistry.CapabilityName(typeof(NativeCapabilityRegistryTests)));

    private sealed class ThreeBackendPlatform : INativePlatform
    {
        public void Register(IServiceCollection services)
        {
            services.TryAddSingleton<IShare>(_ => new NoShare());
            services.TryAddSingleton<IClipboard>(_ => new NoClipboard());
            services.TryAddSingleton<IBadge>(_ => new NoBadge());
        }
    }

    private sealed class FourBackendPlatform : INativePlatform
    {
        public void Register(IServiceCollection services)
        {
            new ThreeBackendPlatform().Register(services);
            services.TryAddSingleton<IVibration>(_ => new NoVibration());
        }
    }

    private sealed class EmptyPlatform : INativePlatform
    {
        public void Register(IServiceCollection services)
        {
        }
    }

    private sealed class PlatformWithHelper : INativePlatform
    {
        public void Register(IServiceCollection services)
        {
            services.TryAddSingleton<IShare>(_ => new NoShare());
            services.TryAddSingleton(_ => new NoShare());
        }
    }

    private sealed class NoShare : IShare
    {
        public ValueTask ShareAsync(ShareData data) => default;

        public ValueTask<bool> CanShareAsync(ShareData? data = null) => ValueTask.FromResult(false);
    }

    private sealed class NoClipboard : IClipboard
    {
        public ValueTask WriteTextAsync(string text) => default;

        public ValueTask<string> ReadTextAsync() => ValueTask.FromResult(string.Empty);
    }

    private sealed class NoBadge : IBadge
    {
        public ValueTask<bool> IsSupportedAsync() => ValueTask.FromResult(false);

        public ValueTask SetAsync(int? count = null) => default;

        public ValueTask ClearAsync() => default;
    }

    private sealed class NoVibration : IVibration
    {
        public ValueTask<bool> VibrateAsync(params int[] pattern) => ValueTask.FromResult(false);

        public ValueTask<bool> CancelAsync() => ValueTask.FromResult(false);
    }
}
