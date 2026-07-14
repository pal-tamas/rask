using Rask.Core;
using Rask.Example.Shared;
using static Rask.Native.Components.Generated;
using NativeColor = Rask.Native.Components.NativeColor;
using NativeIcon = Rask.Native.Components.NativeIcon;
using AppRoutes = Rask.Example.Shared.Features.Routes;

namespace Rask.Example.Native;

/// <summary>
///     The native head mounts this instead of the shared <see cref="App" /> so the showcase gets a real native
///     header + tab bar on iOS/Android (the shared <c>App</c> can't reference <c>Rask.Native</c>). It composes
///     the native bars as siblings of a <see cref="Rask.Native.Components.NativeWebView" /> that hosts the shared
///     App's HTML shell (<c>base.Render()</c>); the web navbar is dropped under the native shell by the
///     <c>IsNative</c> gate in <c>ShowcaseLayout</c>. No <c>IsNative</c> guard is needed here — this type is only
///     ever mounted by the native heads.
/// </summary>
public sealed class NativeShowcaseApp : App
{
    // Brand palette — kept in one place and deliberately aligned with the web theme's accent so the native bars
    // and the WebView content read as one app. NativeColor mirrors NativeIcon: one authored value the platform
    // head resolves to a UIColor / Android Color.
    private static readonly NativeColor Brand = NativeColor.Hex("#4C1D95");   // deep violet, matches the site accent
    private static readonly NativeColor OnBrand = NativeColor.White;

    protected override Component? Render() =>
    [
        NativeHeaderBar(Title: "Rask", Background: Brand, Tint: OnBrand, TitleColor: OnBrand),
        NativeWebView()[base.Render()],
        NativeTabBar(
            // Selected tab picks up the brand accent; the rest stay muted (adaptive so dark mode reads well).
            Tint: Brand,
            UnselectedTint: NativeColor.Adaptive(NativeColor.Hex("#6B7280"), NativeColor.Hex("#9CA3AF")),
            Tabs:
            [
                // Guides is the site root ("/") now that the Welcome landing page is gone.
                NativeTab(Title: "Guides", Icon: NativeIcon.Home, To: AppRoutes.GuidesIndexPage()),
                // A badge (e.g. an unread count) — bind it to live state and it updates on the next render.
                NativeTab(Title: "Todos", Icon: NativeIcon.Custom("checklist", "ic_todo"), To: AppRoutes.TodosPage(), Badge: "2"),
            ])
        // Selected is omitted — the framework highlights the tab matching the current route.
    ];
}
