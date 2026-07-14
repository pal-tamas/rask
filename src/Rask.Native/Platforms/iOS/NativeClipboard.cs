using Rask.Core.Browser;
using UIKit;

namespace Rask.Native;

// Native iOS backend for IClipboard — UIPasteboard instead of the WebView's navigator.clipboard (which
// WKWebView gates behind a user gesture and, for reads, a permission banner). Registered by ApplePlatform.
internal sealed class NativeClipboard : IClipboard
{
    public ValueTask WriteTextAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        UIPasteboard.General.String = text;
        return default;
    }

    public ValueTask<string> ReadTextAsync() =>
        ValueTask.FromResult(UIPasteboard.General.String ?? string.Empty);
}
