using Android.App;
using Android.Content.PM;

namespace Rask.Native;

/// <summary>
///     Bridges an Android runtime-permission request to an awaitable result. A native backend (e.g.
///     <c>NativeNotifications</c>) registers a request code via <see cref="RequestAsync" />, fires
///     <c>Activity.RequestPermissions</c>, and awaits; the host <see cref="Activity" /> forwards its
///     <c>OnRequestPermissionsResult</c> to <see cref="OnResult" />, which completes the awaiter. The wait is
///     bounded by a timeout so a head that forgets to forward can't hang the caller forever (it then reports
///     the current, un-updated status).
/// </summary>
/// <remarks>
///     The host activity must forward results for the await to resolve:
///     <code>
///     public override void OnRequestPermissionsResult(int rc, string[] p, Permission[] r)
///     {
///         NativePermissions.OnResult(rc, r);
///         base.OnRequestPermissionsResult(rc, p, r);
///     }
///     </code>
/// </remarks>
public static class NativePermissions
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<int, TaskCompletionSource<bool>> Pending = [];

    /// <summary>
    ///     Requests <paramref name="permission" /> from <paramref name="activity" /> under
    ///     <paramref name="requestCode" /> and completes with the user's decision once the activity forwards
    ///     the result to <see cref="OnResult" /> (or <c>false</c> if the bounded wait elapses first).
    /// </summary>
    public static async Task<bool> RequestAsync(Activity activity, string permission, int requestCode)
    {
        ArgumentNullException.ThrowIfNull(activity);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Gate)
        {
            Pending[requestCode] = tcs;
        }

        try
        {
            activity.RunOnUiThread(() => activity.RequestPermissions([permission], requestCode));
            var winner = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(false);
            return winner == tcs.Task && tcs.Task.Result;
        }
        finally
        {
            lock (Gate)
            {
                Pending.Remove(requestCode);
            }
        }
    }

    /// <summary>
    ///     Completes the awaiter registered for <paramref name="requestCode" /> with the granted state. Call
    ///     this from the host activity's <c>OnRequestPermissionsResult</c>.
    /// </summary>
    public static void OnResult(int requestCode, Permission[] grantResults)
    {
        ArgumentNullException.ThrowIfNull(grantResults);
        TaskCompletionSource<bool>? tcs;
        lock (Gate)
        {
            Pending.TryGetValue(requestCode, out tcs);
        }

        tcs?.TrySetResult(grantResults.Length > 0 && grantResults[0] == Permission.Granted);
    }
}
