using System.Text;
using System.Text.Json;
using Android.Content;
using Android.Views;
using Android.Webkit;
using Android.Widget;

namespace Rask.Native;

// The Android INativeChrome backend: projects a NativeChromeDescriptor to a top bar and a bottom tab bar /
// toolbar built from framework widgets (no AndroidX.Material dependency — the bars are custom LinearLayouts, so
// this compiles and themes with the default app theme). Assign ChromeView (not View) to SetContentView and
// register this instance as INativeChrome. With no chrome applied the bars are GONE and the WebView fills the
// container.
public sealed partial class RaskAndroidWebView
{
    private LinearLayout? _chromeRoot;
    private LinearLayout? _topBar;
    private LinearLayout? _bottomBar;

    /// <summary>
    ///     The container view to hand to <c>SetContentView</c> when using native header/footer chrome — a
    ///     vertical stack of a top bar, this <see cref="View" />, and a bottom bar. Use this instead of
    ///     <see cref="View" /> and register the same instance as <see cref="INativeChrome" />.
    /// </summary>
    public Android.Views.View ChromeView => _chromeRoot ??= BuildContainer();

    /// <inheritdoc />
    public Func<byte[], Task>? OnChromeEvent { get; set; }

    /// <inheritdoc />
    public ValueTask ApplyChromeAsync(ReadOnlyMemory<byte> chromeDescriptorUtf8)
    {
        var descriptor = JsonSerializer.Deserialize(
            chromeDescriptorUtf8.Span, NativeChromeJsonContext.Default.NativeChromeDescriptor);
        // View updates must run on the UI thread.
        _webView.Post(() => Apply(descriptor));
        return default;
    }

    private LinearLayout BuildContainer()
    {
        var root = new LinearLayout(_context) { Orientation = Orientation.Vertical };
        _topBar = new LinearLayout(_context) { Orientation = Orientation.Horizontal, Visibility = ViewStates.Gone };
        _topBar.SetGravity(GravityFlags.CenterVertical);
        _bottomBar = new LinearLayout(_context) { Orientation = Orientation.Horizontal, Visibility = ViewStates.Gone };

        root.AddView(_topBar, MatchWrap());
        root.AddView(_webView, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0, weight: 1f));
        root.AddView(_bottomBar, MatchWrap());
        return root;
    }

    private void Apply(NativeChromeDescriptor? descriptor)
    {
        ApplyHeader(descriptor?.Header);
        ApplyFooter(descriptor?.Footer);
    }

    private void ApplyHeader(NativeHeaderDescriptor? header)
    {
        if (_topBar is null)
        {
            return;
        }

        _topBar.RemoveAllViews();
        if (header is null)
        {
            _topBar.Visibility = ViewStates.Gone;
            return;
        }

        _topBar.Visibility = ViewStates.Visible;
        if (header.Leading is { } leading)
        {
            _topBar.AddView(BuildBarButton(leading));
        }

        var title = new TextView(_context) { Text = header.Title ?? string.Empty, TextSize = 18f };
        title.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12));
        _topBar.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, weight: 1f));

        if (header.Trailing is { Count: > 0 } trailing)
        {
            foreach (var item in trailing)
            {
                _topBar.AddView(BuildBarButton(item));
            }
        }
    }

    private void ApplyFooter(NativeFooterDescriptor? footer)
    {
        if (_bottomBar is null)
        {
            return;
        }

        _bottomBar.RemoveAllViews();
        if (footer is null)
        {
            _bottomBar.Visibility = ViewStates.Gone;
            return;
        }

        _bottomBar.Visibility = ViewStates.Visible;

        if (string.Equals(footer.Kind, "toolbar", StringComparison.Ordinal))
        {
            foreach (var item in footer.Items ?? [])
            {
                _bottomBar.AddView(BuildBarButton(item), EqualWeight());
            }

            return;
        }

        var tabs = footer.Tabs ?? [];
        for (var i = 0; i < tabs.Count; i++)
        {
            var tab = tabs[i];
            var button = new Button(_context) { Text = tab.Title };
            var path = tab.Path;
            button.Click += (_, _) => Raise($$"""{"type":"navigate","path":"{{Escape(path)}}"}""");
            _bottomBar.AddView(button, EqualWeight());
        }
    }

    private Android.Views.View BuildBarButton(NativeBarItemDescriptor item)
    {
        var button = new Button(_context)
        {
            Text = string.Equals(item.Kind, "back", StringComparison.Ordinal) ? "‹" : item.Title ?? "•",
        };

        var id = item.Id;
        if (id is not null)
        {
            button.Click += (_, _) => Raise($$"""{"type":"nativeTap","id":"{{Escape(id)}}"}""");
        }

        return button;
    }

    private void Raise(string json) => OnChromeEvent?.Invoke(Encoding.UTF8.GetBytes(json));

    private int Dp(int value) => (int)(value * (_context.Resources?.DisplayMetrics?.Density ?? 1f));

    private static LinearLayout.LayoutParams MatchWrap() =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);

    private static LinearLayout.LayoutParams EqualWeight() =>
        new(0, ViewGroup.LayoutParams.WrapContent, weight: 1f);

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
