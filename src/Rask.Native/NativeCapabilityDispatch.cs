using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Client.Browser;
using Rask.Core.Browser;

namespace Rask.Native;

/// <summary>
///     Routes one capability invoke to the native backend the head registered — the switch behind
///     <see cref="NativeCapabilities.TryHandleAsync" />.
/// </summary>
/// <remarks>
///     <para>
///         <b>The wire shape is the JS helper's, not the C# method's.</b> A page reaches these APIs through
///         its own <c>IJSRuntime</c> wrappers (<c>__raskApi.geolocation</c>, <c>__raskBadge.set</c>, …), and
///         those already have a result shape its C# side knows how to read. The bridge substitutes *where the
///         work happens*, not what comes back, so every op here returns exactly what its JS counterpart in
///         <c>rask-api.js</c> / <c>rask-pwa.js</c> would have — which is why the app half needs no change and
///         no <c>IsNative</c> branch.
///     </para>
///     <para>
///         A hand-written switch rather than reflection: the iOS head is full-AOT, where a reflective
///         dispatch is exactly the thing the trimmer removes and only fails on a device. It mirrors how
///         <c>NativeChromeJsonContext</c> stays AOT-safe for the same reason.
///     </para>
/// </remarks>
internal static partial class NativeCapabilityDispatch
{
    /// <summary>
    ///     Run one <c>component.op</c> against the registered backend.
    /// </summary>
    /// <returns>
    ///     The result as JSON, or <see langword="null" /> for an op that returns nothing. A component or op
    ///     this build does not know throws, so the page's promise rejects with a message naming it rather
    ///     than hanging for ever — the failure mode a silently-consumed envelope used to have.
    /// </returns>
    public static async ValueTask<string?> InvokeAsync(
        IServiceProvider services, string component, string op, string? dataJson, Func<string, ValueTask> evaluate)
    {
        // The pushing members go somewhere else entirely — they hold a handle and deliver readings over
        // time, which the request/response switch below has no shape for.
        if (IsStreamOp(component, op))
        {
            return await StreamAsync(services, component, op, dataJson, evaluate).ConfigureAwait(false);
        }

        switch (component)
        {
            case "share":
                return await ShareAsync(services, op, dataJson).ConfigureAwait(false);
            case "geolocation":
                return await GeolocationAsync(services, op, dataJson).ConfigureAwait(false);
            case "clipboard":
                return await ClipboardAsync(services, op, dataJson).ConfigureAwait(false);
            case "vibration":
                return await VibrationAsync(services, op, dataJson).ConfigureAwait(false);
            case "networkInfo":
                return await NetworkAsync(services, op).ConfigureAwait(false);
            case "battery":
                return await BatteryAsync(services, op).ConfigureAwait(false);
            case "screenInfo":
                return await ScreenAsync(services, op).ConfigureAwait(false);
            case "speechSynthesis":
                return await SpeechAsync(services, op, dataJson).ConfigureAwait(false);
            case "notifications":
                return await NotificationsAsync(services, op, dataJson).ConfigureAwait(false);
            case "badge":
                return await BadgeAsync(services, op, dataJson).ConfigureAwait(false);
            case "permissions":
                return await PermissionsAsync(services, op, dataJson).ConfigureAwait(false);
            default:
                throw new NotSupportedException(
                    $"No native backend is bridged for capability '{component}'.");
        }
    }

    private static async ValueTask<string?> ShareAsync(IServiceProvider services, string op, string? dataJson)
    {
        var share = Required<IShare>(services, "share");
        switch (op)
        {
            case "share":
                await share.ShareAsync(Parse(dataJson, NativeCapabilityJsonContext.Default.ShareData)
                    ?? throw Bad("share", "a ShareData payload")).ConfigureAwait(false);
                return null;
            case "canShare":
                return Bool(await share.CanShareAsync(
                    Parse(dataJson, NativeCapabilityJsonContext.Default.ShareData)).ConfigureAwait(false));
            default:
                throw Unknown("share", op);
        }
    }

    private static async ValueTask<string?> GeolocationAsync(
        IServiceProvider services, string op, string? dataJson)
    {
        var geolocation = Required<IGeolocation>(services, "geolocation");
        if (!string.Equals(op, "getCurrentPosition", StringComparison.Ordinal))
        {
            throw Unknown("geolocation", op);
        }

        // The JS helper takes three loose arguments; the envelope carries the options record the C# side
        // already has, so nothing has to agree on argument order.
        var options = Parse(dataJson, NativeCapabilityJsonContext.Default.GeolocationOptions);
        var position = await geolocation.GetCurrentPositionAsync(options).ConfigureAwait(false);
        return JsonSerializer.Serialize(position, NativeCapabilityJsonContext.Default.GeolocationPosition);
    }

    private static async ValueTask<string?> ClipboardAsync(
        IServiceProvider services, string op, string? dataJson)
    {
        var clipboard = Required<IClipboard>(services, "clipboard");
        switch (op)
        {
            case "writeText":
                await clipboard.WriteTextAsync(Text(dataJson) ?? string.Empty).ConfigureAwait(false);
                return null;
            case "readText":
                return JsonSerializer.Serialize(
                    await clipboard.ReadTextAsync().ConfigureAwait(false),
                    NativeCapabilityJsonContext.Default.String);
            default:
                throw Unknown("clipboard", op);
        }
    }

    private static async ValueTask<string?> VibrationAsync(
        IServiceProvider services, string op, string? dataJson)
    {
        var vibration = Required<IVibration>(services, "vibration");
        switch (op)
        {
            case "vibrate":
                var pattern = Parse(dataJson, NativeCapabilityJsonContext.Default.Int32Array) ?? [];
                return Bool(await vibration.VibrateAsync(pattern).ConfigureAwait(false));
            case "cancel":
                return Bool(await vibration.CancelAsync().ConfigureAwait(false));
            default:
                throw Unknown("vibration", op);
        }
    }

    private static async ValueTask<string?> NetworkAsync(IServiceProvider services, string op)
    {
        var network = Required<INetworkInfo>(services, "networkInfo");
        switch (op)
        {
            case "isSupported":
                return Bool(await network.IsSupportedAsync().ConfigureAwait(false));
            case "getStatus":
                var status = await network.GetStatusAsync().ConfigureAwait(false);
                return status is null
                    ? "null"
                    : JsonSerializer.Serialize(status, NativeCapabilityJsonContext.Default.NetworkStatus);
            default:
                throw Unknown("networkInfo", op);
        }
    }

    private static async ValueTask<string?> BatteryAsync(IServiceProvider services, string op)
    {
        var battery = Required<IBattery>(services, "battery");
        switch (op)
        {
            case "isSupported":
                return Bool(await battery.IsSupportedAsync().ConfigureAwait(false));
            case "getStatus":
                var status = await battery.GetStatusAsync().ConfigureAwait(false);
                return status is null
                    ? "null"
                    : JsonSerializer.Serialize(status, NativeCapabilityJsonContext.Default.BatteryStatus);
            default:
                throw Unknown("battery", op);
        }
    }

    private static async ValueTask<string?> ScreenAsync(IServiceProvider services, string op)
    {
        var screen = Required<IScreenInfo>(services, "screenInfo");
        if (!string.Equals(op, "get", StringComparison.Ordinal))
        {
            throw Unknown("screenInfo", op);
        }

        return JsonSerializer.Serialize(
            await screen.GetAsync().ConfigureAwait(false), NativeCapabilityJsonContext.Default.ScreenInfo);
    }

    private static async ValueTask<string?> SpeechAsync(
        IServiceProvider services, string op, string? dataJson)
    {
        var speech = Required<ISpeechSynthesis>(services, "speechSynthesis");
        switch (op)
        {
            case "isSupported":
                return Bool(await speech.IsSupportedAsync().ConfigureAwait(false));
            case "speak":
                var request = Parse(dataJson, NativeCapabilityJsonContext.Default.SpeakRequest)
                    ?? throw Bad("speechSynthesis.speak", "text to speak");
                await speech.SpeakAsync(request.Text, request.Options).ConfigureAwait(false);
                return null;
            case "cancel":
                await speech.CancelAsync().ConfigureAwait(false);
                return null;
            default:
                throw Unknown("speechSynthesis", op);
        }
    }

    private static async ValueTask<string?> NotificationsAsync(
        IServiceProvider services, string op, string? dataJson)
    {
        var notifications = Required<INotifications>(services, "notifications");
        switch (op)
        {
            case "isSupported":
                return Bool(await notifications.IsSupportedAsync().ConfigureAwait(false));
            case "permission":
                return Enum(await notifications.PermissionAsync().ConfigureAwait(false));
            case "requestPermission":
                return Enum(await notifications.RequestPermissionAsync().ConfigureAwait(false));
            case "show":
                var request = Parse(dataJson, NativeCapabilityJsonContext.Default.ShowNotificationRequest)
                    ?? throw Bad("notifications.show", "a title");
                await notifications.ShowAsync(request.Title, request.Options).ConfigureAwait(false);
                return null;
            default:
                throw Unknown("notifications", op);
        }
    }

    private static async ValueTask<string?> BadgeAsync(IServiceProvider services, string op, string? dataJson)
    {
        var badge = Required<IBadge>(services, "badge");
        switch (op)
        {
            case "isSupported":
                return Bool(await badge.IsSupportedAsync().ConfigureAwait(false));
            case "set":
                // null and 0 both mean "a dot, no number" — preserved rather than coerced, because the two
                // are different on the platforms that draw a count.
                await badge.SetAsync(Parse(dataJson, NativeCapabilityJsonContext.Default.NullableInt32))
                    .ConfigureAwait(false);
                return null;
            case "clear":
                await badge.ClearAsync().ConfigureAwait(false);
                return null;
            default:
                throw Unknown("badge", op);
        }
    }

    private static async ValueTask<string?> PermissionsAsync(
        IServiceProvider services, string op, string? dataJson)
    {
        var permissions = Required<IPermissions>(services, "permissions");
        if (!string.Equals(op, "query", StringComparison.Ordinal))
        {
            throw Unknown("permissions", op);
        }

        // The page sends the same lowercase name the Permissions API uses ("camera", "geolocation"), so the
        // envelope speaks the web's vocabulary rather than the enum's spelling.
        var raw = Text(dataJson) ?? string.Empty;
        if (!System.Enum.TryParse<PermissionName>(raw, ignoreCase: true, out var name))
        {
            throw new NotSupportedException($"'{raw}' is not a permission this build knows.");
        }

        return Enum(await permissions.QueryAsync(name).ConfigureAwait(false));
    }

    private static T Required<T>(IServiceProvider services, string component)
        where T : class =>
        services.GetService<T>()
        ?? throw new NotSupportedException(
            $"Capability '{component}' was invoked, but no {typeof(T).Name} is registered on this head.");

    // Enums cross as the lowercase string the web API uses ("granted"), which is what the page's own
    // wrapper already parses — the JS helper returns PermissionStatus.state, not a number.
    private static string Enum<T>(T value)
        where T : struct, Enum =>
        JsonSerializer.Serialize(
            value.ToString().ToLowerInvariant(), NativeCapabilityJsonContext.Default.String);

    private static string Bool(bool value) => value ? "true" : "false";

    private static string? Text(string? dataJson)
    {
        if (string.IsNullOrEmpty(dataJson))
        {
            return null;
        }

        // The page may send a bare string or a JSON string literal; accept both rather than making the
        // caller guess.
        return dataJson[0] == '"'
            ? JsonSerializer.Deserialize(dataJson, NativeCapabilityJsonContext.Default.String)
            : dataJson;
    }

    private static T? Parse<T>(string? dataJson, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
        where T : class =>
        string.IsNullOrEmpty(dataJson) ? null : JsonSerializer.Deserialize(dataJson, type);

    private static T? Parse<T>(string? dataJson, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T?> type)
        where T : struct =>
        string.IsNullOrEmpty(dataJson) ? null : JsonSerializer.Deserialize(dataJson, type);

    private static NotSupportedException Unknown(string component, string op) =>
        new($"'{component}' has no operation '{op}' in this build.");

    private static ArgumentException Bad(string what, string expected) =>
        new($"The '{what}' capability invoke carried no {expected}.");
}

/// <summary>The two arguments <c>speechSynthesis.speak</c> carries, so the envelope needs no argument order.</summary>
internal sealed record SpeakRequest(string Text, SpeechOptions? Options);

/// <summary>The two arguments <c>notifications.show</c> carries.</summary>
internal sealed record ShowNotificationRequest(string Title, NotificationOptions? Options);
