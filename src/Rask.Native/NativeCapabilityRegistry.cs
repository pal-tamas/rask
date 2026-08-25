using System.Globalization;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Native;

/// <summary>
///     What a native head advertises to the page as reachable over the capability bridge.
/// </summary>
/// <remarks>
///     The list is <b>derived</b> from what the platform module registers, not declared beside it. That
///     matters more than it looks:
///     <list type="bullet">
///         <item>
///             Asking the live container instead would advertise everything. The framework registers a
///             JS-backed default for every one of these interfaces, and a platform module uses
///             <c>TryAdd</c>, so <em>something</em> always resolves — "is it registered" cannot tell
///             "native backend" from "the WebView's own JS", which is the only distinction the page cares
///             about.
///         </item>
///         <item>
///             A hand-written list beside <c>Register</c> would answer that, and would drift the first
///             time someone added a sixteenth backend and forgot the other half. Probing the module's own
///             <c>Register</c> cannot drift: adding a backend advertises it, and removing one stops
///             advertising it, with nothing to keep in step.
///         </item>
///     </list>
///     Probing is cheap and side-effect-free — <c>Register</c> adds descriptors, and a descriptor's factory
///     is not invoked until something resolves it, which a throwaway collection never does.
/// </remarks>
public static class NativeCapabilityRegistry
{
    /// <summary>
    ///     The capability names a page may invoke on this platform module, in registration order.
    /// </summary>
    /// <param name="platform">The module a head passed to <see cref="NativeAppHost.UsePlatform" />.</param>
    public static IReadOnlyList<string> AdvertisedFor(INativePlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);

        var probe = new ServiceCollection();
        platform.Register(probe);

        var names = new List<string>();
        foreach (var descriptor in probe)
        {
            if (descriptor.ServiceType is { IsInterface: true } service
                && CapabilityName(service) is { } name
                && !names.Contains(name, StringComparer.Ordinal))
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>
    ///     The wire name for a backend interface — <c>IGeolocation</c> → <c>geolocation</c>. One rule rather
    ///     than a lookup table, so a new interface needs no entry anywhere and the name a page uses is
    ///     predictable from the type it is asking for.
    /// </summary>
    /// <returns>The name, or <see langword="null" /> if the type is not an <c>I</c>-prefixed interface.</returns>
    public static string? CapabilityName(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        var name = serviceType.Name;

        // IShare → Share; anything not shaped like an interface name is not a capability.
        if (name.Length < 2 || name[0] != 'I' || !char.IsUpper(name[1]))
        {
            return null;
        }

        var bare = name[1..];
        return string.Create(
            bare.Length,
            bare,
            static (span, source) =>
            {
                source.AsSpan().CopyTo(span);
                span[0] = char.ToLower(span[0], CultureInfo.InvariantCulture);
            });
    }
}
