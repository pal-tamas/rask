using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>A queryable browser permission (the names the Permissions API accepts).</summary>
public enum PermissionName
{
    /// <summary><c>geolocation</c> — see <see cref="IGeolocation" />.</summary>
    Geolocation,

    /// <summary><c>notifications</c>.</summary>
    Notifications,

    /// <summary><c>camera</c>.</summary>
    Camera,

    /// <summary><c>microphone</c>.</summary>
    Microphone,

    /// <summary><c>clipboard-read</c> — see <see cref="IClipboard" />.</summary>
    ClipboardRead,

    /// <summary><c>clipboard-write</c> — see <see cref="IClipboard" />.</summary>
    ClipboardWrite,

    /// <summary><c>persistent-storage</c>.</summary>
    PersistentStorage
}

/// <summary>Current state of a queried permission (<c>PermissionStatus.state</c>).</summary>
public enum PermissionState
{
    /// <summary>The user has not yet decided — the API will prompt on first use.</summary>
    Prompt,

    /// <summary>Access is granted; the feature can be used without prompting.</summary>
    Granted,

    /// <summary>Access is denied; the feature is blocked until the user changes the setting.</summary>
    Denied
}

/// <summary>
///     Typed access to the Permissions API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Permissions/query" />) — check
///     whether a feature is granted, denied, or will prompt, <em>before</em> triggering it. Pairs with
///     <see cref="IClipboard" /> and <see cref="IGeolocation" /> to avoid surprising the user with a
///     prompt. Inject it through a component constructor and call from an event handler or lifecycle hook.
///     <para>
///         <b>Engines answer for different names.</b> WebKit (Safari) answers only for
///         <see cref="PermissionName.Camera" /> and <see cref="PermissionName.Microphone" />; the rest
///         fault — see <see cref="QueryAsync" />. Chromium answers for the full set. Treat anything other
///         than <see cref="PermissionState.Granted" /> as "not granted" rather than as a promise of a
///         dialog. See <c>docs/apis/permissions.md</c>.
///     </para>
/// </summary>
public interface IPermissions
{
    /// <summary>
    ///     Queries the current state of <paramref name="name" /> (<c>navigator.permissions.query</c>).
    ///     A browser that doesn't recognise the permission faults the awaited task with a
    ///     <see cref="JSException" /> — WebKit does this for every name except
    ///     <see cref="PermissionName.Camera" /> and <see cref="PermissionName.Microphone" />, so catch it if
    ///     you target Safari.
    /// </summary>
    ValueTask<PermissionState> QueryAsync(PermissionName name);
}

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
