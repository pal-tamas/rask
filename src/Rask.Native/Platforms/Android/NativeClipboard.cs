using Android.App;
using Android.Content;
using Rask.Core.Browser;

namespace Rask.Native;

// Native Android backend for IClipboard — ClipboardManager instead of the WebView's navigator.clipboard.
// Clipboard access must happen on the UI thread. Registered by AndroidPlatform.
internal sealed class NativeClipboard(Activity activity) : IClipboard
{
    public ValueTask WriteTextAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tcs = new TaskCompletionSource();
        activity.RunOnUiThread(() =>
        {
            Manager().PrimaryClip = ClipData.NewPlainText("text", text);
            tcs.TrySetResult();
        });
        return new ValueTask(tcs.Task);
    }

    public ValueTask<string> ReadTextAsync()
    {
        var tcs = new TaskCompletionSource<string>();
        activity.RunOnUiThread(() =>
        {
            var clip = Manager().PrimaryClip;
            var text = clip is { ItemCount: > 0 }
                ? clip.GetItemAt(0)?.CoerceToText(activity)?.ToString()
                : null;
            tcs.TrySetResult(text ?? string.Empty);
        });
        return new ValueTask<string>(tcs.Task);
    }

    private ClipboardManager Manager() =>
        (ClipboardManager)activity.GetSystemService(Context.ClipboardService)!;
}
