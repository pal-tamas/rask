using Microsoft.JSInterop;

namespace Rask.Core.Browser;

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
    public async ValueTask<NotificationPermissionState> PermissionAsync() =>
        Map(await js.InvokeAsync<string?>("Notification.permission"));

    /// <inheritdoc />
    public async ValueTask<NotificationPermissionState> RequestPermissionAsync() =>
        Map(await js.InvokeAsync<string?>("Notification.requestPermission"));

    /// <inheritdoc />
    public ValueTask ShowAsync(string title, NotificationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(title);
        return js.InvokeVoidAsync("__raskNotify.show", title, options ?? new NotificationOptions());
    }

    private static NotificationPermissionState Map(string? value) => value switch
    {
        "granted" => NotificationPermissionState.Granted,
        "denied" => NotificationPermissionState.Denied,
        _ => NotificationPermissionState.Default
    };
}
