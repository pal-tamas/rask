using Android.App;
using Android.Content;
using Rask.Client.Browser;
using Rask.Core.Browser;

namespace Rask.Native;

/// <summary>
///     Native Android backend for <see cref="IShare" /> — hands <see cref="ShareData" /> to the system share
///     sheet (an <c>ACTION_SEND</c> chooser), overriding the JS-backed default (<c>navigator.share</c>).
///     Register it on <c>host.Services</c> before <c>RunLocalAsync</c> (Native + Local) or hand it to
///     <see cref="NativeCapabilities.TryHandleAsync" /> (Native + Server). The native path needs no transient
///     user activation and works even though <c>android.webkit.WebView</c> has no <c>navigator.share</c>.
/// </summary>
public sealed class NativeShare(Activity activity) : IShare
{
    /// <inheritdoc />
    public ValueTask ShareAsync(ShareData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var body = string.Join("\n", new[] { data.Text, data.Url }.Where(s => !string.IsNullOrEmpty(s)));
        if (string.IsNullOrEmpty(body) && string.IsNullOrEmpty(data.Title))
        {
            return default;
        }

        var send = new Intent(Intent.ActionSend);
        send.SetType("text/plain");
        send.PutExtra(Intent.ExtraText, body);
        if (!string.IsNullOrEmpty(data.Title))
        {
            send.PutExtra(Intent.ExtraSubject, data.Title);
        }

        var chooser = Intent.CreateChooser(send, data.Title);
        activity.RunOnUiThread(() => activity.StartActivity(chooser));
        return default;
    }

    /// <inheritdoc />
    public ValueTask<bool> CanShareAsync(ShareData? data = null) =>
        ValueTask.FromResult(data is null || HasContent(data));

    private static bool HasContent(ShareData data) =>
        !string.IsNullOrEmpty(data.Title) || !string.IsNullOrEmpty(data.Text) || !string.IsNullOrEmpty(data.Url);
}
