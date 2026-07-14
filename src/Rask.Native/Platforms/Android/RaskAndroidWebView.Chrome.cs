using System.Text;
using System.Text.Json;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using Rask.Native.Components;

namespace Rask.Native;

// The Android INativeChrome backend: projects a NativeChromeDescriptor to a top bar and a bottom tab bar /
// toolbar built from framework widgets (no AndroidX.Material dependency — the bars are custom LinearLayouts, so
// this compiles and themes with the default app theme). Assign ChromeView (not View) to SetContentView and
// register this instance as INativeChrome. With no chrome applied the bars are GONE and the WebView fills the
// container.
public sealed partial class RaskAndroidWebView
{
    // Held while an overflow PopupMenu is showing so its managed MenuItemClick callback isn't GC'd (a local
    // PopupMenu can be collected after the click handler returns, silently dropping the item taps).
    private PopupMenu? _menuPopup;
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
        // Let a tab's corner badge overflow its cell without being clipped by the bar.
        _bottomBar.SetClipChildren(false);

        root.AddView(_topBar, MatchWrap());
        root.AddView(_webView, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0, weight: 1f));
        root.AddView(_bottomBar, MatchWrap());

        // The content is drawn edge-to-edge (the colored bars fill behind the system bars). Inset the header's
        // top and the footer's bottom by the status-/navigation-bar heights so their content clears the system
        // bars while the bar background still shows behind them — parity with the iOS safe-area handling. When a
        // bar is hidden the WebView takes that edge and keeps its own env(safe-area-inset-*) CSS padding, so the
        // insets are left unconsumed.
        root.SetOnApplyWindowInsetsListener(new SystemBarInsetListener(_topBar, _bottomBar));
        return root;
    }

    // Pads the header/footer for the system bars from framework WindowInsets (no AndroidX).
    private sealed class SystemBarInsetListener(Android.Views.View topBar, Android.Views.View bottomBar)
        : Java.Lang.Object, Android.Views.View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(Android.Views.View v, WindowInsets insets)
        {
            int top, bottom;
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                var bars = insets.GetInsets(WindowInsets.Type.SystemBars());
                top = bars.Top;
                bottom = bars.Bottom;
            }
            else
            {
#pragma warning disable CA1422 // SystemWindowInset* is the pre-API-30 path.
                top = insets.SystemWindowInsetTop;
                bottom = insets.SystemWindowInsetBottom;
#pragma warning restore CA1422
            }

            topBar.SetPadding(topBar.PaddingLeft, top, topBar.PaddingRight, topBar.PaddingBottom);
            bottomBar.SetPadding(bottomBar.PaddingLeft, bottomBar.PaddingTop, bottomBar.PaddingRight, bottom);
            return insets;
        }
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
        ApplyBarBackground(_topBar, header.Background);
        // Content color: the bar's own colors when set, else one that contrasts with the background — the
        // custom LinearLayout widgets carry no themed color of their own, so an explicit color keeps them
        // readable on a styled/dark bar.
        var onBar = OnBarColor(header.Background);
        var tint = ResolveColor(header.Tint) ?? onBar;
        if (header.Leading is { } leading)
        {
            _topBar.AddView(BuildBarButton(leading, tint));
        }

        var titleSlot = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, weight: 1f);
        if (header.Segments is { Count: > 0 } segments)
        {
            // A segmented control takes the title's slot (parity with the iOS titleView).
            _topBar.AddView(BuildSegmentedControl(segments, header.SelectedSegment, tint), titleSlot);
        }
        else
        {
            var title = new TextView(_context)
            {
                Text = header.Title ?? string.Empty,
                TextSize = 18f,
                // Stable content-desc so screen readers + the Appium E2E can address the native header.
                ContentDescription = "rask-native-header",
            };
            title.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12));
            title.SetTextColor(ResolveColor(header.TitleColor) ?? onBar);
            _topBar.AddView(title, titleSlot);
        }

        if (header.Trailing is { Count: > 0 } trailing)
        {
            foreach (var item in trailing)
            {
                _topBar.AddView(BuildBarButton(item, tint));
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
        ApplyBarBackground(_bottomBar, footer.Background);

        if (string.Equals(footer.Kind, "toolbar", StringComparison.Ordinal))
        {
            var toolTint = ResolveColor(footer.Tint) ?? OnBarColor(footer.Background);
            foreach (var item in footer.Items ?? [])
            {
                _bottomBar.AddView(BuildBarButton(item, toolTint), EqualWeight());
            }

            return;
        }

        // Tab bar: the selected tab uses the full content color (Tint or the on-bar default); the rest use a
        // muted color (UnselectedTint or a dimmed on-bar default), so the active tab is always highlighted even
        // when only Tint — or nothing — is set. Parity with iOS's selected/unselected item tints.
        var onBar = OnBarColor(footer.Background);
        var selectedColor = ResolveColor(footer.Tint) ?? onBar;
        var unselectedColor = ResolveColor(footer.UnselectedTint) ?? Muted(onBar);
        var tabs = footer.Tabs ?? [];
        for (var i = 0; i < tabs.Count; i++)
        {
            _bottomBar.AddView(BuildTabItem(tabs[i], i == footer.Selected ? selectedColor : unselectedColor), EqualWeight());
        }
    }

    // A tab is an icon (resolved from its Android drawable name) over a label — a plain bottom-nav item built
    // without AndroidX. When the drawable can't be resolved it degrades to a text-only label.
    private Android.Views.View BuildTabItem(NativeTabDescriptor tab, Color color)
    {
        var container = new LinearLayout(_context) { Orientation = Orientation.Vertical };
        container.SetGravity(GravityFlags.Center);
        container.Clickable = true;
        // The whole tab is one tap target and one accessibility node addressed by its title (screen readers +
        // the Appium E2E) — the icon/label are decorative, so they don't become separate nodes.
        container.ContentDescription = tab.Title;
        container.ImportantForAccessibility = ImportantForAccessibility.Yes;

        var badgeText = string.IsNullOrEmpty(tab.Badge) ? null : tab.Badge;
        var resId = ResolveDrawable(tab.AndroidIcon);
        if (resId != 0)
        {
            var icon = new ImageView(_context) { ImportantForAccessibility = ImportantForAccessibility.No };
            icon.SetImageResource(resId);
            icon.SetColorFilter(color);
            var size = Dp(24);
            if (badgeText is null)
            {
                container.AddView(icon, new LinearLayout.LayoutParams(size, size));
            }
            else
            {
                // Overlay the badge on the top-right of the icon (no AndroidX BadgeDrawable dependency). The
                // frame is icon-sized and clipping is disabled so the badge can hug the corner and overflow.
                var frame = new FrameLayout(_context);
                frame.SetClipChildren(false);
                container.SetClipChildren(false);
                frame.AddView(icon, new FrameLayout.LayoutParams(size, size));
                frame.AddView(BuildBadge(badgeText),
                    new FrameLayout.LayoutParams(
                        ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent,
                        GravityFlags.Top | GravityFlags.End));
                container.AddView(frame, new LinearLayout.LayoutParams(size, size));
            }
        }
        else if (badgeText is not null)
        {
            // No icon to hang the badge on — show it above the label.
            container.AddView(BuildBadge(badgeText));
        }

        var label = new TextView(_context)
        {
            Text = tab.Title,
            TextSize = 12f,
            Gravity = GravityFlags.Center,
            ImportantForAccessibility = ImportantForAccessibility.No,
        };
        label.SetTextColor(color);
        container.AddView(label);

        var path = tab.Path;
        container.Click += (_, _) => Raise($$"""{"type":"navigate","path":"{{Escape(path)}}"}""");
        return container;
    }

    private Android.Views.View BuildBarButton(NativeBarItemDescriptor item, Color tint)
    {
        if (string.Equals(item.Kind, "menu", StringComparison.Ordinal))
        {
            return BuildMenuButton(item, tint);
        }

        var isBack = string.Equals(item.Kind, "back", StringComparison.Ordinal);
        var resId = isBack ? 0 : ResolveDrawable(item.AndroidIcon);
        Android.Views.View view;
        if (resId != 0)
        {
            // An icon button when the drawable resolves — matches iOS's SF-Symbol bar buttons. A visible title
            // is often null for an icon button, so prefer it for the spoken/queryable label, then the tap id.
            var imageButton = new ImageButton(_context);
            imageButton.SetImageResource(resId);
            imageButton.SetBackgroundColor(Color.Transparent);
            imageButton.SetColorFilter(tint);
            imageButton.ContentDescription = item.Title ?? item.Id;
            view = imageButton;
        }
        else
        {
            var button = new Button(_context)
            {
                Text = isBack ? "‹" : item.Title ?? "•",
                // Address the button by its tap id (back → a stable token) for screen readers + the E2E.
                ContentDescription = isBack ? "rask-native-back" : item.Id ?? item.Title,
            };
            button.SetTextColor(tint);
            view = button;
        }

        if (isBack)
        {
            // A back button pops the WebView history (like hardware Back) via a "back" event.
            view.Click += (_, _) => Raise("""{"type":"back"}""");
            return view;
        }

        var id = item.Id;
        if (id is not null)
        {
            view.Click += (_, _) => Raise($$"""{"type":"nativeTap","id":"{{Escape(id)}}"}""");
        }

        return view;
    }

    // A small rounded badge (e.g. an unread count) drawn without AndroidX — a white-on-red pill TextView.
    private TextView BuildBadge(string text)
    {
        var badge = new TextView(_context)
        {
            Text = text,
            TextSize = 9f,
            Gravity = GravityFlags.Center,
            ImportantForAccessibility = ImportantForAccessibility.No,
        };
        badge.SetTextColor(Color.White);
        badge.SetPadding(Dp(4), 0, Dp(4), 0);
        badge.SetMinWidth(Dp(16));
        badge.SetMinHeight(Dp(16));

        var background = new GradientDrawable();
        background.SetShape(ShapeType.Rectangle);
        background.SetColor(Color.Argb(255, 0xD3, 0x2F, 0x2F)); // Material red 700
        background.SetCornerRadius(Dp(8));
        badge.Background = background;
        return badge;
    }

    // Material primary-text black (~87% opacity) — the default content color on a light bar.
    private static readonly Color DarkContent = Color.Argb(222, 0, 0, 0);

    // A readable content color for a bar with the given background: dark content on a light bar, light on a
    // dark one. The custom LinearLayout widgets carry no themed color, so an explicit default keeps them legible.
    private Color OnBarColor(string? backgroundToken) =>
        ResolveColor(backgroundToken) is { } bg ? ContrastOn(bg) : DarkContent;

    // Dark or white, whichever reads on the given fill color.
    private static Color ContrastOn(Color fill)
    {
        var luminance = ((0.299 * fill.R) + (0.587 * fill.G) + (0.114 * fill.B)) / 255.0;
        return luminance > 0.5 ? DarkContent : Color.White;
    }

    // A segmented control: a rounded, tint-bordered row of buttons; the selected one is filled with the tint.
    // Built without AndroidX so it themes with the plain app; best for 2–3 short labels.
    private Android.Views.View BuildSegmentedControl(
        IReadOnlyList<NativeSegmentDescriptor> segments, int selected, Color tint)
    {
        var row = new LinearLayout(_context) { Orientation = Orientation.Horizontal };
        var border = new GradientDrawable();
        border.SetShape(ShapeType.Rectangle);
        border.SetCornerRadius(Dp(6));
        border.SetStroke(Dp(1), tint);
        row.Background = border;
        row.SetPadding(Dp(2), Dp(2), Dp(2), Dp(2));

        for (var i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            var button = new Button(_context) { Text = seg.Title, TextSize = 12f, ContentDescription = seg.Title };
            button.SetAllCaps(false);
            button.SetMinWidth(0);
            button.SetMinimumWidth(0);
            button.SetPadding(Dp(10), Dp(4), Dp(10), Dp(4));
            if (i == selected)
            {
                var fill = new GradientDrawable();
                fill.SetShape(ShapeType.Rectangle);
                fill.SetCornerRadius(Dp(4));
                fill.SetColor(tint);
                button.Background = fill;
                button.SetTextColor(ContrastOn(tint));
            }
            else
            {
                button.SetBackgroundColor(Color.Transparent);
                button.SetTextColor(tint);
            }

            var id = seg.Id;
            if (id is not null)
            {
                button.Click += (_, _) => Raise($$"""{"type":"nativeTap","id":"{{Escape(id)}}"}""");
            }

            row.AddView(button, EqualWeight());
        }

        return row;
    }

    // A dimmed variant for unselected tabs, so the active tab stands out even when no tints are set.
    private static Color Muted(Color color) => Color.Argb((int)(color.A * 0.6), color.R, color.G, color.B);

    // An overflow button (⋮ or a resolved icon) whose tap opens a framework PopupMenu (no AndroidX). Each entry
    // raises the same nativeTap the session dispatches, so selecting one re-enters the ordinary handler path.
    private Android.Views.View BuildMenuButton(NativeBarItemDescriptor item, Color tint)
    {
        var resId = ResolveDrawable(item.AndroidIcon);
        Android.Views.View button;
        if (resId != 0)
        {
            var imageButton = new ImageButton(_context);
            imageButton.SetImageResource(resId);
            imageButton.SetBackgroundColor(Color.Transparent);
            imageButton.SetColorFilter(tint);
            imageButton.ContentDescription = item.Title;
            button = imageButton;
        }
        else
        {
            var textButton = new Button(_context) { Text = "⋮", ContentDescription = item.Title }; // ⋮
            textButton.SetTextColor(tint);
            button = textButton;
        }

        var entries = item.Menu ?? [];
        button.Click += (_, _) =>
        {
            var popup = new PopupMenu(_context, button);
            for (var i = 0; i < entries.Count; i++)
            {
                var added = popup.Menu?.Add(0, i, i, entries[i].Title);
                var iconRes = ResolveDrawable(entries[i].AndroidIcon);
                if (iconRes != 0)
                {
                    added?.SetIcon(iconRes);
                }
            }

            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                popup.SetForceShowIcon(true); // otherwise PopupMenu hides item icons by default
            }

            popup.MenuItemClick += (_, e) =>
            {
                var idx = e.Item?.ItemId ?? -1;
                if (idx >= 0 && idx < entries.Count && entries[idx].Id is { } id)
                {
                    Raise($$"""{"type":"nativeTap","id":"{{Escape(id)}}"}""");
                }
            };
            // Keep the popup (and thus its managed click callback) alive until it dismisses.
            popup.DismissEvent += (_, _) => _menuPopup = null;
            _menuPopup = popup;
            popup.Show();
        };
        return button;
    }

    // Set an explicit bar background from a color token, or clear it (null background ⇒ the theme default shows).
    private void ApplyBarBackground(LinearLayout bar, string? token)
    {
        if (ResolveColor(token) is { } color)
        {
            bar.SetBackgroundColor(color);
        }
        else
        {
            bar.Background = null;
        }
    }

    // A wire token → Android Color, resolving an adaptive ("light|dark") pair against the current night mode.
    // (Android recreates the Activity on a uiMode change, so this re-runs with the new mode — no live swap needed.)
    private Color? ResolveColor(string? token)
    {
        if (!NativeColor.TryResolve(token, out var light, out var dark))
        {
            return null;
        }

        var c = IsNightMode ? dark : light;
        return Color.Argb(c.A, c.R, c.G, c.B);
    }

    private bool IsNightMode =>
        (_context.Resources?.Configuration?.UiMode & Android.Content.Res.UiMode.NightMask)
        == Android.Content.Res.UiMode.NightYes;

    private int ResolveDrawable(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return 0;
        }

#pragma warning disable CA1422 // GetIdentifier is the supported path for a name→drawable lookup on all API levels.
        return _context.Resources?.GetIdentifier(name, "drawable", _context.PackageName) ?? 0;
#pragma warning restore CA1422
    }

    private void Raise(string json) => OnChromeEvent?.Invoke(Encoding.UTF8.GetBytes(json));

    private int Dp(int value) => (int)(value * (_context.Resources?.DisplayMetrics?.Density ?? 1f));

    private static LinearLayout.LayoutParams MatchWrap() =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);

    private static LinearLayout.LayoutParams EqualWeight() =>
        new(0, ViewGroup.LayoutParams.WrapContent, weight: 1f);

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
