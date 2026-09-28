using Microsoft.JSInterop;

namespace Rask.Core.Browser;

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
