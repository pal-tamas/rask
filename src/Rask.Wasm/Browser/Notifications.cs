using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

/// <summary>
///     Options for a local notification
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Notification/Notification" />). Unset
///     members take the browser default.
/// </summary>
public sealed record NotificationOptions
{
    /// <summary>Body text shown below the title.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Body { get; init; }

    /// <summary>Icon URL.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; init; }

    /// <summary>Badge URL (monochrome, for constrained UIs).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Badge { get; init; }

    /// <summary>Tag — a new notification with the same tag replaces the previous one.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tag { get; init; }

    /// <summary>Keep the notification visible until the user interacts with it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RequireInteraction { get; init; }

    /// <summary>Suppress sound/vibration.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Silent { get; init; }
}

/// <summary>
///     Typed access to local notifications (the Notifications API,
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Notifications_API" />) — show a
///     notification directly from the page (no server / push needed). <b>WASM-only:</b>
///     <c>Notification.requestPermission()</c> needs a live user gesture, which the Server/WebSocket
///     round-trip loses. For notifications delivered while the app is closed, use
///     <see cref="IWebPush" /> (push goes through the service worker).
/// </summary>
/// <remarks>
///     Requires a secure context. Gate on <see cref="IsSupportedAsync" /> /
///     <see cref="RequestPermissionAsync" /> and wrap in try/catch — an unsupported browser or denied
///     permission surfaces as a <see cref="JSException" />.
/// </remarks>
public interface INotifications
{
    /// <summary>Whether the browser supports notifications.</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>The current permission without prompting (<c>Notification.permission</c>).</summary>
    ValueTask<NotificationPermission> PermissionAsync();

    /// <summary>Prompts for (or reports) notification permission (<c>Notification.requestPermission</c>).</summary>
    ValueTask<NotificationPermission> RequestPermissionAsync();

    /// <summary>Shows a notification (<c>new Notification(title, options)</c>). Requires granted permission.</summary>
    ValueTask ShowAsync(string title, NotificationOptions? options = null);
}

/// <summary>
///     Default <see cref="INotifications" />, backed by the unified <see cref="IJSRuntime" />.
///     <c>new Notification(...)</c> is a constructor, so showing goes through the framework's
///     <c>__raskNotify.show</c> helper; permission read/request are plain property/Promise calls.
/// </summary>
public sealed class Notifications(IJSRuntime js) : INotifications
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskNotify.isSupported");

    /// <inheritdoc />
    public async ValueTask<NotificationPermission> PermissionAsync() =>
        Map(await js.InvokeAsync<string?>("Notification.permission"));

    /// <inheritdoc />
    public async ValueTask<NotificationPermission> RequestPermissionAsync() =>
        Map(await js.InvokeAsync<string?>("Notification.requestPermission"));

    /// <inheritdoc />
    public ValueTask ShowAsync(string title, NotificationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(title);
        return js.InvokeVoidAsync("__raskNotify.show", title, options ?? new NotificationOptions());
    }

    private static NotificationPermission Map(string? value) => value switch
    {
        "granted" => NotificationPermission.Granted,
        "denied" => NotificationPermission.Denied,
        _ => NotificationPermission.Default
    };
}
