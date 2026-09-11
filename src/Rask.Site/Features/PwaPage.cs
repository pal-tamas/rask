using Rask.Core.Routing;
using Rask.Site;

namespace Rask.Site.Features;

/// <summary>
///     ONE page for every PWA and device-capability demo, with a rail down the side.
/// </summary>
/// <remarks>
///     <para>
///     These were thirteen sidebar rows — "PWA demo", "Install prompt", "Wake lock", "Orientation",
///     "Fullscreen", "Picture-in-Picture", "EyeDropper", "Idle detection", "Camera &amp; mic",
///     "Serial port", "USB device", "HID device", "Bluetooth" — each a page whose whole body was a
///     heading, a paragraph and one <see cref="CodeSample" />. Thirteen rows is not a table of contents,
///     it is a list a reader has to read in full to discover that twelve of them are the same idea:
///     a typed C# wrapper over a browser capability that only exists in WASM.
///     </para>
///     <para>
///     So the page teaches the idea once, at the top, and the thirteen demos are sections under it. The
///     rail is generated from the same list the sections are, which is the point of holding them as data:
///     a table of contents assembled by hand beside the content it indexes is a table of contents that
///     goes stale, and silently, because nothing fails when a link points at a heading that moved.
///     </para>
///     <para>
///     IT ANSWERS ALL THIRTEEN OLD URLS. <c>[Route]</c> repeats, so <c>/docs/wake-lock</c> and the rest
///     still resolve here instead of 404-ing — the first declared route is canonical and the others are
///     alternates the router matches. Deep links that were shared, bookmarked or indexed keep working;
///     dropping them would have been a silent regression for every reader who had one, and the only
///     place it would have shown up is someone else's browser history.
///     </para>
/// </remarks>
[Route("pwa")]
[Route("install")]
[Route("wake-lock")]
[Route("orientation")]
[Route("fullscreen")]
[Route("picture-in-picture")]
[Route("eye-dropper")]
[Route("idle")]
[Route("media-devices")]
[Route("serial")]
[Route("usb")]
[Route("hid")]
[Route("bluetooth")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class PwaPage : Component
{
    /// <summary>One demo: the anchor the rail points at, its title, and its body.</summary>
    /// <remarks>
    ///     The body is a factory rather than a built component, and that is not style. A component built
    ///     once into a static field and rendered on every pass is a runtime-built component that never gets
    ///     mounted on the passes after the first — so each render asks the list for fresh instances.
    /// </remarks>
    private sealed record Section(string Slug, string Title, Func<Component> Body);

    protected override Component? HeadAssets =>
        PageMeta.For(
            "PWA and device APIs in C# for WebAssembly — Rask",
            "Typed C# wrappers for browser device APIs, each with a live WebAssembly demo: install prompt, "
            + "push, wake lock, fullscreen, camera, Web Serial and WebUSB.",
            Routes.PwaPage());

    protected override Component? Render()
    {
        var sections = Sections();

        return
        [
            H1.Class("text-3xl font-bold mb-1")["PWA & device APIs"],
            P.Class("text-ui-muted max-w-3xl")[
                "Every browser capability Rask wraps in typed C#, live. They are ",
                Strong["WASM-only"],
                " — each one needs a live user gesture, the live document, or a device handle that a Server ",
                "round-trip cannot carry — so each demo runs in your browser, in this page's own WebAssembly ",
                "app. This site is itself an installable, offline PWA: install it from your address bar and ",
                "the same code runs as an app."
            ],
            P.Class("text-ui-muted max-w-3xl mt-2")[
                "Every one follows the same shape. Ask whether the capability exists before you offer it ",
                "(",
                Code["IsSupportedAsync"],
                "), call it from a real click, and dispose what it hands back — most of these return an ",
                Code["IAsyncDisposable"],
                " that releases the hardware or the lock. A request without user activation rejects, and a ",
                "chooser the reader dismisses returns ",
                Code["null"],
                " rather than throwing: dismissal is an answer, not an error."
            ],
            Rail(sections),
            // A wrapper, because a collection expression's elements are each ONE component and this is a
            // sequence of them — the indexer is what takes an enumerable (`..` spread does not work here),
            // and `Fragment` is RaskMarkup's, which a Component cannot reach.
            Div[sections.Select(section => Section_(section.Slug, section.Title, section.Body()))]
        ];
    }

    /// <summary>The on-this-page rail, generated from the section list.</summary>
    private static Component Rail(IReadOnlyList<Section> sections) =>
        Nav
            .Class("mt-6 mb-8 rounded-xl bg-ui-bg ring-1 ring-ui-line p-4")
            .Aria(new Dictionary<string, string?> { ["label"] = "On this page" })[
                Div.Class("text-xs font-semibold uppercase tracking-widest text-ui-muted mb-2")["On this page"],
                Ul.Class("grid gap-1 sm:grid-cols-2 lg:grid-cols-3")[
                    sections.Select(section =>
                        Li.Key(section.Slug)[
                            A
                                .Href("#" + section.Slug)
                                .Class("text-sm text-ui-brand-ink underline-offset-2 hover:underline")[
                                    section.Title
                                ]
                        ])
                ]
            ];

    /// <summary>
    ///     One section. <c>scroll-mt-24</c> clears the sticky top bar when the rail jumps here — without it
    ///     the heading lands underneath the bar and the reader sees the paragraph after it.
    /// </summary>
    /// <remarks>
    ///     <c>data-section</c> as well as the heading's id, and both are load-bearing. The id is the anchor
    ///     the rail jumps to; the attribute is what lets anything SCOPE to one demo. Thirteen demos on one
    ///     page means thirteen <c>.sample-code</c> blocks and thirteen result panels, so an assertion (or a
    ///     stylesheet) that names one of those classes without a section around it now matches all thirteen
    ///     — which is exactly how the browser suite failed the first time this page existed.
    /// </remarks>
    private static Component Section_(string slug, string title, Component body) =>
        Div.Key(slug).Class("mt-10").Attributes(("data-section", slug))[
            H2.Id(slug).Class("scroll-mt-24 text-2xl font-semibold mb-1")[title],
            body
        ];

    private static List<Section> Sections() =>
    [
        new("notifications", "Notifications, push & badge", () =>
        [
            P.Class("text-ui-muted")[
                "Local notifications, Web Push readiness and the installed-app badge — the three that make an ",
                "installed app feel like one."
            ],
            CodeSample
                .Files(["PwaDemo.cs"])
                .Notes("Local notifications, Web Push readiness, and the installed-app badge — all WASM-only "
                    + "(they need a live user gesture or the installed-PWA instance the Server round-trip can't carry).")
                .Result(PwaDemo)
        ]),

        new("install", "Install prompt", () =>
        [
            P.Class("text-ui-muted")[
                "Show a custom \"Install app\" button via IInstallPrompt instead of the browser's default ",
                "mini-infobar. The framework captures and defers the beforeinstallprompt event at boot, so you ",
                "reveal your button when CanInstallAsync() is true and trigger PromptAsync() from the click. ",
                "WASM-only — the install flow needs the live document and transient activation."
            ],
            CodeSample
                .Files(["InstallPromptDemo.cs"])
                .Notes("CanInstallAsync()/IsInstalledAsync() are one-shot polls; PromptAsync() replays the deferred "
                    + "event and returns the user's InstallOutcome. The browser only offers it over HTTPS with a "
                    + "valid manifest + service worker, once per load.")
                .Result(InstallPromptDemo)
        ]),

        new("wake-lock", "Wake lock", () =>
        [
            P.Class("text-ui-muted")[
                "Keep the screen from dimming or locking via IWakeLock (the Screen Wake Lock API) — for timers, ",
                "reading, or media. The lock is released automatically when the page is hidden and re-acquired ",
                "when it returns."
            ],
            CodeSample
                .Files(["WakeLockDemo.cs"])
                .Notes("RequestAsync returns an IWakeLockSentinel (IAsyncDisposable); dispose it to release. "
                    + "WASM-only — the lock is tied to the live document.")
                .Result(WakeLockDemo)
        ]),

        new("orientation", "Orientation", () =>
        [
            P.Class("text-ui-muted")[
                "Read the screen orientation via IScreenOrientation and, for an installed or fullscreen app, ",
                "lock it. Locking is usually rejected outside fullscreen and is often unsupported on desktop."
            ],
            CodeSample
                .Files(["OrientationDemo.cs"])
                .Notes("GetAsync returns the OrientationInfo (type + angle); LockAsync/UnlockAsync change it. "
                    + "WASM-only — locking needs the live, usually fullscreen, document.")
                .Result(OrientationDemo)
        ]),

        new("fullscreen", "Fullscreen", () =>
        [
            P.Class("text-ui-muted")[
                "Present an element — or the whole page — fullscreen via IFullscreen (the Fullscreen API), ",
                "passing an ElementRef to target one box. WASM-only: requestFullscreen needs a live user ",
                "gesture. Pairs with Orientation — locking the orientation generally requires fullscreen first."
            ],
            CodeSample
                .Files(["FullscreenDemo.cs"])
                .Notes("RequestAsync(ElementRef?) fullscreens that element (or the page when null); ExitAsync "
                    + "leaves. Gate on IsSupportedAsync and wrap in try/catch — a request without activation rejects.")
                .Result(FullscreenDemo)
        ]),

        new("picture-in-picture", "Picture-in-Picture", () =>
        [
            P.Class("text-ui-muted")[
                "Float a video into an always-on-top miniplayer the user keeps visible while they scroll or ",
                "switch tabs, via IPictureInPicture (the Picture-in-Picture API). WASM-only: ",
                "requestPictureInPicture needs a live user gesture. This demo synthesizes its video from an ",
                "animated canvas (sibling scoped JS), so it needs no shipped media file."
            ],
            CodeSample
                .Files(["PictureInPictureDemo.cs", "PictureInPictureDemo.ts"])
                .Notes("RequestAsync(ElementRef) sends that <video> to the miniplayer; ExitAsync brings it back. "
                    + "Gate on IsSupportedAsync and wrap in try/catch — a request without activation rejects.")
                .Result(PictureInPictureDemo)
        ]),

        new("eye-dropper", "EyeDropper", () =>
        [
            P.Class("text-ui-muted")[
                "Let the user pick a color from anywhere on screen with the system magnifier loupe, via ",
                "IEyeDropper (the EyeDropper API) — handy for a design tool or theme editor. WASM-only: ",
                "open() needs a live user gesture, and it's Chromium-family only at the time of writing."
            ],
            CodeSample
                .Files(["EyeDropperDemo.cs"])
                .Notes("OpenAsync() resolves with the picked sRGB hex (e.g. \"#3366ff\"), or null if the user "
                    + "cancels (Escape) — cancellation is not an error. Gate on IsSupportedAsync.")
                .Result(EyeDropperDemo)
        ]),

        new("idle", "Idle detection", () =>
        [
            P.Class("text-ui-muted")[
                "Be notified when the user goes idle (no input for a threshold) or the screen locks, via ",
                "IIdleDetector (the Idle Detection API) — e.g. to auto-lock a session, pause a sync, or update ",
                "presence in a collaborative app. WASM-only: the idle-detection permission needs a live gesture ",
                "and the detector needs the live document."
            ],
            CodeSample
                .Files(["IdleDetectorDemo.cs"])
                .Notes("RequestPermissionAsync() must run from a gesture; WatchAsync(onChange, thresholdSeconds) "
                    + "then pushes an IdleReading on each user/screen state change. The spec enforces a 60-second "
                    + "minimum threshold. Dispose the handle to stop.")
                .Result(IdleDetectorDemo)
        ]),

        new("media-devices", "Camera & microphone", () =>
        [
            P.Class("text-ui-muted")[
                "Capture the camera, microphone, or screen and show it in a <video> via IMediaDevices ",
                "(getUserMedia / getDisplayMedia) — for photo capture, video calls, or screen recording. ",
                "WASM-only: capture needs a live user gesture and a secure context. Dispose the stream handle ",
                "to stop every track and release the hardware (the camera indicator turns off)."
            ],
            CodeSample
                .Files(["MediaDevicesDemo.cs"])
                .Notes("GetUserMediaAsync(constraints) / GetDisplayMediaAsync() return a disposable "
                    + "IMediaStreamHandle; AttachToAsync(ElementRef) wires the stream to a <video> and plays it. "
                    + "The live MediaStream stays JS-side under a minted id. Gate on IsSupportedAsync and "
                    + "try/catch — a denied request throws.")
                .Result(MediaDevicesDemo)
        ]),

        new("serial", "Web Serial", () =>
        [
            P.Class("text-ui-muted")[
                "Talk to a serial device — an Arduino or microcontroller, a GPS, a USB-to-serial adapter — ",
                "straight from C# via ISerial (the Web Serial API): pick a port, write a line, and watch inbound ",
                "bytes stream into the log. WASM-only: requestPort() needs a live user gesture and the live port ",
                "stream, and it's Chromium-family only at the time of writing."
            ],
            CodeSample
                .Files(["SerialDemo.cs"])
                .Notes("RequestPortAsync shows the browser port chooser, opens the port, and starts a read loop "
                    + "that pushes inbound bytes to your callback; it returns null if the user dismisses the "
                    + "chooser (not an error). Dispose the port to stop reading and release it. Gate on "
                    + "IsSupportedAsync.")
                .Result(SerialDemo)
        ]),

        new("usb", "WebUSB", () =>
        [
            P.Class("text-ui-muted")[
                "Pair with and drive a USB device — custom hardware, a dev board, an instrument — straight from C# ",
                "via IUsb (the WebUSB API): show its descriptor, open it, claim an interface, and run ",
                "bulk / interrupt / control transfers. WASM-only: requestDevice() needs a live user gesture and the ",
                "live device handle, and it's Chromium-family only at the time of writing."
            ],
            CodeSample
                .Files(["UsbDemo.cs"])
                .Notes("RequestDeviceAsync shows the browser device chooser and returns an IUsbDevice (null if the "
                    + "user dismisses it). Transfer payloads cross as byte[]; dispose the device to release it. "
                    + "Actual transfers are device-specific, so the demo shows discovery + lifecycle. Gate on "
                    + "IsSupportedAsync.")
                .Result(UsbDemo)
        ]),

        new("hid", "WebHID", () =>
        [
            P.Class("text-ui-muted")[
                "Talk to a human-interface device that no higher-level API covers — a gamepad with custom reports, ",
                "a keyboard with extra keys, simulation controls, point-of-sale hardware — via IHid (the WebHID ",
                "API): open it, send output / feature reports, and subscribe to its live input-report stream. ",
                "WASM-only: requestDevice() needs a live user gesture and the live device handle, and it's ",
                "Chromium-family only at the time of writing."
            ],
            CodeSample
                .Files(["HidDemo.cs"])
                .Notes("RequestDevicesAsync shows the browser chooser and returns the granted devices (empty if "
                    + "dismissed). Open a device, then WatchInputReportsAsync pushes each input report (and an "
                    + "optional disconnect signal) to your callback; dispose the watch and the device to release. "
                    + "Report payloads cross as byte[]. Gate on IsSupportedAsync.")
                .Result(HidDemo)
        ]),

        new("bluetooth", "Web Bluetooth", () =>
        [
            P.Class("text-ui-muted")[
                "Pair with a Bluetooth Low Energy device and talk to its GATT services from C# — connect, read / ",
                "write characteristics, and subscribe to notifications (heart-rate monitors, thermometers, fitness ",
                "sensors, custom hardware) — via IBluetooth (the Web Bluetooth API). WASM-only: requestDevice() ",
                "needs a live user gesture and the live device handle, and it's Chromium-family only at the time of ",
                "writing."
            ],
            CodeSample
                .Files(["BluetoothDemo.cs"])
                .Notes("RequestDeviceAsync shows the chooser and returns an IBluetoothDevice (null if dismissed). "
                    + "Connect, then GetCharacteristicAsync(service, characteristic) → read/write/WatchAsync "
                    + "(notifications). This demo reads the standard Battery Service. Values cross as byte[]; "
                    + "dispose the device to drop the connection. Gate on IsSupportedAsync.")
                .Result(BluetoothDemo)
        ]),
    ];
}
