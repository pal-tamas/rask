using Android.App;
using Android.Content;
using Rask.Client.Browser;
using Rask.Core.Browser;

namespace Company.RaskNative;

// Native Android backend for IShare — hands ShareData to the system share sheet (an ACTION_SEND chooser),
// overriding Rask.Native's JS-backed default (navigator.share). Registered in MainActivity before
// RunLocalAsync (last registration wins). The native path needs no transient user activation and works
// even though android.webkit.WebView doesn't implement navigator.share.
//
// This is the template for any native device backend: implement a Rask.Core.Browser interface with the
// platform API and register it on host.Services before RunLocalAsync.
public sealed class NativeShare(Activity activity) : IShare
{
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

    // The native share sheet can always present when there is something to share.
    public ValueTask<bool> CanShareAsync(ShareData? data = null) =>
        ValueTask.FromResult(data is null || HasContent(data));

    private static bool HasContent(ShareData data) =>
        !string.IsNullOrEmpty(data.Title) || !string.IsNullOrEmpty(data.Text) || !string.IsNullOrEmpty(data.Url);
}
