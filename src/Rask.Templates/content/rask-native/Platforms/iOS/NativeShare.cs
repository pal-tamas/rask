using CoreFoundation;
using CoreGraphics;
using Foundation;
using Rask.Client.Browser;
using Rask.Core.Browser;
using UIKit;

namespace Company.RaskNative;

// Native iOS backend for IShare — hands ShareData to the system share sheet (UIActivityViewController),
// overriding Rask.Native's JS-backed default (navigator.share). Registered in AppDelegate before
// RunLocalAsync (last registration wins). The native path needs no transient user activation, so it works
// from any handler and even where WKWebView doesn't expose navigator.share.
//
// This is the template for any native device backend: implement a Rask.Core.Browser interface with the
// platform API and register it on host.Services before RunLocalAsync.
public sealed class NativeShare(Func<UIViewController?> presenter) : IShare
{
    public ValueTask ShareAsync(ShareData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        // UIActivityViewController has no dedicated title slot; fold Title into the shared text.
        var text = string.Join("\n", new[] { data.Title, data.Text }.Where(s => !string.IsNullOrEmpty(s)));
        var items = new List<NSObject>();
        if (!string.IsNullOrEmpty(text))
        {
            items.Add(new NSString(text));
        }

        if (!string.IsNullOrEmpty(data.Url) && NSUrl.FromString(data.Url) is { } url)
        {
            items.Add(url);
        }

        if (items.Count == 0)
        {
            return default;
        }

        DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            if (presenter() is not { } vc)
            {
                return;
            }

            var activity = new UIActivityViewController([.. items], null);
            // On iPad the sheet presents as a popover and throws without an anchor — point it at the view.
            if (activity.PopoverPresentationController is { } popover && vc.View is { } anchor)
            {
                popover.SourceView = anchor;
                popover.SourceRect = new CGRect(anchor.Bounds.GetMidX(), anchor.Bounds.GetMidY(), 0, 0);
            }

            vc.PresentViewController(activity, animated: true, completionHandler: null);
        });
        return default;
    }

    // The native share sheet can always present when there is something to share.
    public ValueTask<bool> CanShareAsync(ShareData? data = null) =>
        ValueTask.FromResult(data is null || HasContent(data));

    private static bool HasContent(ShareData data) =>
        !string.IsNullOrEmpty(data.Title) || !string.IsNullOrEmpty(data.Text) || !string.IsNullOrEmpty(data.Url);
}
