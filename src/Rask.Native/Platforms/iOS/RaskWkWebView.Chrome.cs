using System.Text;
using System.Text.Json;
using CoreFoundation;
using CoreGraphics;
using Foundation;
using Rask.Native.Components;
using UIKit;
using WebKit;

namespace Rask.Native;

// The iOS INativeChrome backend: projects a NativeChromeDescriptor to a real UINavigationBar (top) and a
// UITabBar / UIToolbar (bottom), with the WKWebView pinned between them and both bars occupying the safe-area
// guides. Assign ChromeView (not View) to the view controller and register this instance as INativeChrome.
// When no chrome is applied the bars stay hidden and the WebView fills the container, so it degrades to the
// plain full-screen WebView.
public sealed partial class RaskWkWebView
{
    private RaskChromeContainerView? _chromeView;

    /// <summary>
    ///     The container view to assign to a view controller's <c>View</c> when using native header/footer
    ///     chrome — it hosts a <c>UINavigationBar</c>, this <see cref="View" />, and a <c>UITabBar</c>/
    ///     <c>UIToolbar</c>. Assign this instead of <see cref="View" /> and register the same instance as
    ///     <see cref="INativeChrome" />.
    /// </summary>
    public UIView ChromeView => _chromeView ??= new RaskChromeContainerView(View);

    /// <inheritdoc />
    public Func<byte[], Task>? OnChromeEvent { get; set; }

    /// <inheritdoc />
    public ValueTask ApplyChromeAsync(ReadOnlyMemory<byte> chromeDescriptorUtf8)
    {
        var descriptor = JsonSerializer.Deserialize(
            chromeDescriptorUtf8.Span, NativeChromeJsonContext.Default.NativeChromeDescriptor);
        var container = _chromeView ??= new RaskChromeContainerView(View);
        // UIKit is main-thread only; the push happens on the render thread.
        DispatchQueue.MainQueue.DispatchAsync(() => container.Apply(descriptor, RaiseChromeEvent));
        return default;
    }

    private void RaiseChromeEvent(string json) => OnChromeEvent?.Invoke(Encoding.UTF8.GetBytes(json));
}

// A frame-laid-out container: navbar below the top safe-area inset, the WebView in the middle, and a tab bar /
// toolbar above the bottom safe-area inset. Frame math (not Auto Layout) keeps the show/hide of each bar a
// simple per-layout computation.
internal sealed class RaskChromeContainerView : UIView
{
    private const float HeaderHeight = 44f;
    private const float TabBarHeight = 49f;
    private const float ToolbarHeight = 44f;

    private readonly WKWebView _webView;
    private readonly UINavigationBar _navBar;
    private readonly UITabBar _tabBar;
    private readonly UIToolbar _toolbar;

    private bool _headerVisible;
    private bool _footerVisible;
    private bool _footerIsToolbar;

    // The pure-native content view, when a NativeScreen has been mounted. It sits in the same slot as the
    // WebView and only ONE of the two is visible at a time — see ShowWebView/ShowNative. Neither is ever
    // removed: both the WebView's DOM and the retained native tree are diff baselines the session patches
    // against, so tearing either down would leave it patching a view that no longer exists.
    private UIView? _nativeContent;
    private Action<string>? _raise;
    private IReadOnlyList<NativeTabDescriptor>? _tabs;
    private UITabBarItem[] _tabItems = [];

    public RaskChromeContainerView(WKWebView webView)
    {
        _webView = webView;
        // A stable, localization-independent handle for screen readers and the Appium on-device E2E.
        _navBar = new UINavigationBar { Hidden = true, AccessibilityIdentifier = "rask-native-header" };
        _tabBar = new UITabBar { Hidden = true };
        _toolbar = new UIToolbar { Hidden = true };
        BackgroundColor = UIColor.SystemBackground;
        // Subscribe once (ItemSelected is an event); the handler reads the current tabs each tap.
        _tabBar.ItemSelected += (_, e) =>
        {
            var idx = Array.IndexOf(_tabItems, e.Item);
            if (_tabs is not null && idx >= 0 && idx < _tabs.Count)
            {
                _raise?.Invoke($$"""{"type":"navigate","path":"{{Escape(_tabs[idx].Path)}}"}""");
            }
        };
        AddSubview(_webView);
        AddSubview(_navBar);
        AddSubview(_tabBar);
        AddSubview(_toolbar);
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        var safe = SafeAreaInsets;
        var width = Bounds.Width;
        nfloat top = safe.Top;
        nfloat headerH = _headerVisible ? HeaderHeight : 0;
        nfloat footerH = _footerVisible ? (_footerIsToolbar ? ToolbarHeight : TabBarHeight) : 0;

        _navBar.Frame = new CGRect(0, top, width, headerH);
        _navBar.Hidden = !_headerVisible;

        var footerY = Bounds.Height - safe.Bottom - footerH;
        _tabBar.Frame = new CGRect(0, footerY, width, footerH);
        _toolbar.Frame = new CGRect(0, footerY, width, footerH);
        _tabBar.Hidden = !(_footerVisible && !_footerIsToolbar);
        _toolbar.Hidden = !(_footerVisible && _footerIsToolbar);

        var webTop = top + headerH;
        var contentFrame = new CGRect(0, webTop, width, footerY - webTop);
        _webView.Frame = contentFrame;
        if (_nativeContent is not null)
        {
            _nativeContent.Frame = contentFrame;
        }
    }

    /// <summary>
    ///     This frame's content is HTML: show the WebView and hide the native tree, keeping the latter alive
    ///     so returning to a native route patches it instead of rebuilding it.
    /// </summary>
    public void ShowWebView()
    {
        _webView.Hidden = false;
        if (_nativeContent is not null)
        {
            _nativeContent.Hidden = true;
        }
    }

    /// <summary>
    ///     This frame's content is a pure-native screen: show <paramref name="root" /> and hide the WebView —
    ///     which is only hidden, never unloaded, so its DOM still matches the session's HTML diff baseline.
    /// </summary>
    public void ShowNative(UIView root)
    {
        if (!ReferenceEquals(_nativeContent, root))
        {
            // A re-mount replaces the tree; the previous one is genuinely dead, so it goes.
            _nativeContent?.RemoveFromSuperview();
            _nativeContent = root;
            root.TranslatesAutoresizingMaskIntoConstraints = true;
            AddSubview(root);
            SetNeedsLayout();
        }

        _nativeContent.Hidden = false;
        _webView.Hidden = true;
        // The bars are laid out around whichever content view is showing, and a mount can arrive before the
        // first layout pass has run.
        SetNeedsLayout();
    }

    public void Apply(NativeChromeDescriptor? descriptor, Action<string> raise)
    {
        _raise = raise;
        ApplyHeader(descriptor?.Header);
        ApplyFooter(descriptor?.Footer);
        SetNeedsLayout();
    }

    private void ApplyHeader(NativeHeaderDescriptor? header)
    {
        if (header is null)
        {
            _headerVisible = false;
            _navBar.Items = [];
            return;
        }

        _headerVisible = true;
        ApplyNavBarAppearance(header);
        var item = new UINavigationItem(header.Title ?? string.Empty);
        if (header.Leading is { } leading)
        {
            item.LeftBarButtonItem = BuildBarButton(leading);
        }

        if (header.Trailing is { Count: > 0 } trailing)
        {
            var right = new UIBarButtonItem[trailing.Count];
            // UIKit lays trailing items right-to-left; keep the author's order left-to-right.
            for (var i = 0; i < trailing.Count; i++)
            {
                right[i] = BuildBarButton(trailing[trailing.Count - 1 - i]);
            }

            item.RightBarButtonItems = right;
        }

        if (header.Segments is { Count: > 0 } segments)
        {
            // A segmented control replaces the title (iOS's standard titleView pattern).
            item.TitleView = BuildSegmentedControl(segments, header.SelectedSegment, ResolveUIColor(header.Tint));
        }

        _navBar.Items = [item];
    }

    private UISegmentedControl BuildSegmentedControl(
        IReadOnlyList<NativeSegmentDescriptor> segments, int selected, UIColor? tint)
    {
        var control = new UISegmentedControl();
        for (var i = 0; i < segments.Count; i++)
        {
            control.InsertSegment(segments[i].Title ?? string.Empty, i, false);
        }

        control.SelectedSegment = Math.Clamp(selected, 0, segments.Count - 1);
        if (tint is not null)
        {
            control.SelectedSegmentTintColor = tint;
        }

        control.ValueChanged += (_, _) =>
        {
            var i = (int)control.SelectedSegment;
            if (i >= 0 && i < segments.Count && segments[i].Id is { } id)
            {
                _raise?.Invoke($$"""{"type":"nativeTap","id":"{{Escape(id)}}"}""");
            }
        };
        return control;
    }

    private void ApplyFooter(NativeFooterDescriptor? footer)
    {
        if (footer is null)
        {
            _footerVisible = false;
            _tabBar.Items = [];
            _toolbar.Items = [];
            return;
        }

        _footerVisible = true;
        _footerIsToolbar = string.Equals(footer.Kind, "toolbar", StringComparison.Ordinal);

        if (_footerIsToolbar)
        {
            ApplyToolbarAppearance(footer);
            _tabs = null;
            _tabItems = [];
            var items = footer.Items ?? [];
            var buttons = new UIBarButtonItem[items.Count];
            for (var i = 0; i < items.Count; i++)
            {
                buttons[i] = BuildBarButton(items[i]);
            }

            _toolbar.Items = buttons;
            return;
        }

        ApplyTabBarAppearance(footer);
        var tabs = footer.Tabs ?? [];
        _tabs = tabs;
        _tabItems = new UITabBarItem[tabs.Count];
        for (var i = 0; i < tabs.Count; i++)
        {
            _tabItems[i] = new UITabBarItem(tabs[i].Title, ImageFor(tabs[i].IosIcon), i)
            {
                // Address each tab by its title (screen readers + the Appium E2E), independent of the icon.
                AccessibilityIdentifier = tabs[i].Title,
                // Optional badge (e.g. an unread count); null clears it.
                BadgeValue = string.IsNullOrEmpty(tabs[i].Badge) ? null : tabs[i].Badge,
            };
        }

        _tabBar.Items = _tabItems;
        if (tabs.Count > 0)
        {
            _tabBar.SelectedItem = _tabItems[Math.Clamp(footer.Selected, 0, tabs.Count - 1)];
        }
    }

    // Project the descriptor's optional colors onto the UINavigationBar. When nothing is styled we leave the
    // bar's system appearance untouched (styling is opt-in) — critically, we don't override iOS's default
    // transparent scroll-edge appearance on an unstyled bar. Tint drives the bar buttons.
    private void ApplyNavBarAppearance(NativeHeaderDescriptor header)
    {
        var bg = ResolveUIColor(header.Background);
        var title = ResolveUIColor(header.TitleColor);
        var tint = ResolveUIColor(header.Tint);
        if (bg is null && title is null && tint is null)
        {
            return;
        }

        var appearance = new UINavigationBarAppearance();
        ConfigureBackground(appearance, bg);
        if (title is not null)
        {
            appearance.TitleTextAttributes = new UIStringAttributes { ForegroundColor = title };
            appearance.LargeTitleTextAttributes = new UIStringAttributes { ForegroundColor = title };
        }

        _navBar.StandardAppearance = appearance;
        // Only pin the scroll-edge appearance when a background color is set (so a colored bar stays colored
        // edge-to-edge); otherwise keep iOS's default scroll-edge look.
        if (bg is not null)
        {
            _navBar.ScrollEdgeAppearance = appearance;
        }

        if (tint is not null)
        {
            _navBar.TintColor = tint;
        }
    }

    private void ApplyTabBarAppearance(NativeFooterDescriptor footer)
    {
        var bg = ResolveUIColor(footer.Background);
        var tint = ResolveUIColor(footer.Tint);
        var unselected = ResolveUIColor(footer.UnselectedTint);
        if (bg is null && tint is null && unselected is null)
        {
            return;
        }

        var appearance = new UITabBarAppearance();
        ConfigureBackground(appearance, bg);
        _tabBar.StandardAppearance = appearance;
        if (bg is not null && OperatingSystem.IsIOSVersionAtLeast(15, 0))
        {
            _tabBar.ScrollEdgeAppearance = appearance;
        }

        // TintColor is the selected-item color; UnselectedItemTintColor the rest. Leaving unselected null keeps
        // the system gray, so the selected tab still stands out even when only Tint is set.
        if (tint is not null)
        {
            _tabBar.TintColor = tint;
        }

        if (unselected is not null)
        {
            _tabBar.UnselectedItemTintColor = unselected;
        }
    }

    private void ApplyToolbarAppearance(NativeFooterDescriptor footer)
    {
        var bg = ResolveUIColor(footer.Background);
        var tint = ResolveUIColor(footer.Tint);
        if (bg is null && tint is null)
        {
            return;
        }

        var appearance = new UIToolbarAppearance();
        ConfigureBackground(appearance, bg);
        _toolbar.StandardAppearance = appearance;
        if (tint is not null)
        {
            _toolbar.TintColor = tint;
        }
    }

    // Shared bar-background config (UINavigationBar/UITabBar/UIToolbar appearances all derive from UIBarAppearance).
    private static void ConfigureBackground(UIBarAppearance appearance, UIColor? bg)
    {
        if (bg is not null)
        {
            appearance.ConfigureWithOpaqueBackground();
            appearance.BackgroundColor = bg;
        }
        else
        {
            appearance.ConfigureWithDefaultBackground();
        }
    }

    // A wire token → UIColor. A fixed token yields a static color; an adaptive ("light|dark") token yields a
    // dynamic UIColor that resolves per the current UI style, so the bar tracks light/dark automatically.
    // Also used by the pure-native surface backend (UiKitViewOps), which resolves the same NativeColor tokens.
    internal static UIColor? ResolveUIColor(string? token)
    {
        if (!NativeColor.TryResolve(token, out var light, out var dark))
        {
            return null;
        }

        var lightColor = FromChannels(light);
        if (light.Equals(dark))
        {
            return lightColor;
        }

        var darkColor = FromChannels(dark);
        return UIColor.FromDynamicProvider(traits =>
            traits.UserInterfaceStyle == UIUserInterfaceStyle.Dark ? darkColor : lightColor);
    }

    private static UIColor FromChannels((byte R, byte G, byte B, byte A) c) =>
        UIColor.FromRGBA((nfloat)(c.R / 255f), (nfloat)(c.G / 255f), (nfloat)(c.B / 255f), (nfloat)(c.A / 255f));

    private UIBarButtonItem BuildBarButton(NativeBarItemDescriptor item)
    {
        if (string.Equals(item.Kind, "back", StringComparison.Ordinal))
        {
            // A back chevron that pops the WebView history (like hardware Back) via a "back" event.
            var backItem = new UIBarButtonItem
            {
                Style = UIBarButtonItemStyle.Plain,
                Image = UIImage.GetSystemImage("chevron.backward"),
                AccessibilityIdentifier = "rask-native-back",
            };
            backItem.Clicked += (_, _) => _raise?.Invoke("""{"type":"back"}""");
            return backItem;
        }

        if (string.Equals(item.Kind, "menu", StringComparison.Ordinal))
        {
            return BuildMenuButton(item);
        }

        // Address the button by its tap id (falling back to its title) for screen readers + the E2E.
        var button = new UIBarButtonItem { Style = UIBarButtonItemStyle.Plain, AccessibilityIdentifier = item.Id ?? item.Title };
        if (ImageFor(item.IosIcon) is { } image)
        {
            button.Image = image;
        }
        else
        {
            button.Title = item.Title ?? string.Empty;
        }

        var id = item.Id;
        if (id is not null)
        {
            button.Clicked += (_, _) => _raise?.Invoke($$"""{"type":"nativeTap","id":"{{Escape(id)}}"}""");
        }

        return button;
    }

    // An overflow button whose primary tap opens a UIMenu pull-down (iOS 14+). Each entry raises the same
    // nativeTap the session dispatches, so a menu selection re-enters the ordinary handler path.
    private UIBarButtonItem BuildMenuButton(NativeBarItemDescriptor item)
    {
        var entries = item.Menu ?? [];
        var actions = new UIMenuElement[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            var id = entries[i].Id;
            var action = UIAction.Create(
                entries[i].Title ?? string.Empty,
                ImageFor(entries[i].IosIcon),
                null,
                _ =>
                {
                    if (id is not null)
                    {
                        _raise?.Invoke($$"""{"type":"nativeTap","id":"{{Escape(id)}}"}""");
                    }
                });
            if (entries[i].Destructive)
            {
                action.Attributes = UIMenuElementAttributes.Destructive;
            }

            actions[i] = action;
        }

        var button = new UIBarButtonItem { AccessibilityIdentifier = item.Title, Menu = UIMenu.Create(actions) };
        if (ImageFor(item.IosIcon) is { } image)
        {
            button.Image = image;
        }
        else
        {
            button.Title = item.Title ?? "More";
        }

        return button;
    }

    private static UIImage? ImageFor(string? sfSymbol) =>
        string.IsNullOrEmpty(sfSymbol) ? null : UIImage.GetSystemImage(sfSymbol);

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
