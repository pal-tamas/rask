using CoreFoundation;
using CoreGraphics;
using Foundation;
using Rask.Client.Browser;
using Rask.Core.Browser;
using UIKit;

namespace Rask.Native;

/// <summary>
///     Native iOS backend for <see cref="IShare" /> — hands <see cref="ShareData" /> to the system share
///     sheet (<c>UIActivityViewController</c>), overriding the JS-backed default (<c>navigator.share</c>).
///     Register it on <c>host.Services</c> before <c>RunLocalAsync</c> (Native + Local) or hand it to
///     <see cref="NativeCapabilities.TryHandleAsync" /> (Native + Server). Works from any handler and even
///     where <c>WKWebView</c> doesn't expose <c>navigator.share</c>.
/// </summary>
/// <param name="presenter">Supplies the view controller to present the sheet from.</param>
public sealed class NativeShare(Func<UIViewController?> presenter) : IShare
{
    /// <inheritdoc />
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

    /// <inheritdoc />
    public ValueTask<bool> CanShareAsync(ShareData? data = null) =>
        ValueTask.FromResult(data is null || HasContent(data));

    private static bool HasContent(ShareData data) =>
        !string.IsNullOrEmpty(data.Title) || !string.IsNullOrEmpty(data.Text) || !string.IsNullOrEmpty(data.Url);
}
