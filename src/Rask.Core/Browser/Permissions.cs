using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="IPermissions" />, backed by the unified <see cref="IJSRuntime" />.
///     <c>navigator.permissions.query</c> resolves to a live <c>PermissionStatus</c> object, so the call
///     goes through the framework's <c>__raskApi.permissionState</c> helper, which returns just the
///     <c>state</c> string.
/// </summary>
public sealed class Permissions(IJSRuntime js) : IPermissions
{
    /// <inheritdoc />
    public async ValueTask<PermissionState> QueryAsync(PermissionName name)
    {
        var state = await js.InvokeAsync<string?>("__raskApi.permissionState", ToSpecName(name));
        return state switch
        {
            "granted" => PermissionState.Granted,
            "denied" => PermissionState.Denied,
            _ => PermissionState.Prompt
        };
    }

    // The Permissions API uses hyphenated lowercase descriptor names.
    private static string ToSpecName(PermissionName name) => name switch
    {
        PermissionName.Geolocation => "geolocation",
        PermissionName.Notifications => "notifications",
        PermissionName.Camera => "camera",
        PermissionName.Microphone => "microphone",
        PermissionName.ClipboardRead => "clipboard-read",
        PermissionName.ClipboardWrite => "clipboard-write",
        PermissionName.PersistentStorage => "persistent-storage",
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown permission name.")
    };
}
