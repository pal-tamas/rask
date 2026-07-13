using Rask.Core;
using Rask.Example.Shared;
using static Rask.Native.Components.Generated;
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
    protected override Component? Render() =>
    [
        NativeHeaderBar(Title: "Rask"),
        NativeWebView()[base.Render()],
        NativeTabBar(
            Tabs:
            [
                NativeTab(Title: "Home", Icon: NativeIcon.Home, To: AppRoutes.HomePage()),
                NativeTab(Title: "Guides", Icon: NativeIcon.List, To: AppRoutes.GuidesIndexPage()),
                NativeTab(Title: "Todos", Icon: NativeIcon.Custom("checklist", "ic_todo"), To: AppRoutes.TodosPage()),
            ])
        // Selected is omitted — the framework highlights the tab matching the current route.
    ];
}
