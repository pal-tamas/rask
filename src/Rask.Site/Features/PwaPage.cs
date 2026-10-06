using Rask.Core.Routing;

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
            "MDN's browser device APIs called from C#, each with a live WebAssembly demo: install prompt, "
            + "push, wake lock, fullscreen, camera, Web Serial and WebUSB.",
            Routes.PwaPage());

    protected override Component? Render()
    {
        var sections = Sections();

        return
        [
            H1.Class("text-3xl font-bold mb-1")["PWA & device APIs"],
            P.Class("text-ui-muted max-w-3xl")[
                "The browser's device and app capabilities called from C#, live — MDN's own APIs, through ",
                Code["Rask.Web"],
                ". Nearly all are ",
                Strong["WASM-only"],
                " — each of those needs a live user gesture, the live document, or a device handle that a Server ",
                "round-trip cannot carry; the screen wake lock and the declarative install trigger are the ",
                "exceptions — so each demo runs in your browser, in this page's own WebAssembly ",
                "app. This site is itself an installable, offline PWA: install it from your address bar and ",
                "the same code runs as an app."
            ],
            P.Class("text-ui-muted max-w-3xl mt-2")[
                "Every one follows the same shape. Ask whether the capability exists before you offer it ",
                "(",
                Code["IsSupported"],
                "), call it from a real click, and dispose what it hands back — most of these return an ",
                Code["IAsyncDisposable"],
                " that releases the hardware or the lock. A request without user activation rejects, and so ",
                "does a chooser the reader dismisses — catch ",
                Code["JSException"],
                " and treat dismissal as an answer, not an error."
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
            .Aria("label", "On this page")[
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
        NotificationsTopic(),
        InstallPromptTopic(),
        WakeLockTopic(),
        OrientationTopic(),
        FullscreenTopic(),
        PictureInPictureTopic(),
        EyeDropperTopic(),
        IdleDetectionTopic(),
        MediaDevicesTopic(),
        WebSerialTopic(),
        WebUsbTopic(),
        WebHidTopic(),
        WebBluetoothTopic(),
    ];

    private static Section NotificationsTopic() =>
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
        ]);

    private static Section InstallPromptTopic() =>
        new("install", "Install prompt", () =>
        [
            P.Class("text-ui-muted")[
                "Show your own \"Install app\" button with MDN's beforeinstallprompt from Rask.Web. The browser ",
                "fires it once, as the page loads, before any component listens — so Rask.Web keeps it from boot ",
                "and hands it to Window.OnBeforeInstallPrompt; the button's click calls Prompt() on it. ",
                "Prompt() runs in WebAssembly — it needs the click's transient activation."
            ],
            CodeSample
                .Files(["InstallPromptDemo.cs"])
                .Notes("The kept BeforeInstallPromptEvent is spent once prompted; Prompt() answers MDN's "
                    + "PromptResponseObject, whose UserChoice is Accepted or Dismissed. The browser only offers it over "
                    + "HTTPS with a valid manifest + service worker, once per load. On the server host, Trigger.Install "
                    + "shows the same kept prompt from markup.")
                .Result(InstallPromptDemo)
        ]);

    private static Section WakeLockTopic() =>
        new("wake-lock", "Wake lock", () =>
        [
            P.Class("text-ui-muted")[
                "Keep the screen from dimming or locking with MDN's Screen Wake Lock API from Rask.Web — for ",
                "timers, reading, or media. Every browser lets go of the lock when the page is hidden; Rask.Web asks ",
                "for it again when the page is visible, so the sentinel holds until you release it."
            ],
            CodeSample
                .Files(["WakeLockDemo.cs"])
                .Notes("Navigator.WakeLock.Request(WakeLockType.Screen) answers a kept WakeLockSentinel: Release() "
                    + "lets the screen sleep (Released turns true, OnRelease fires), and disposing of it lets the "
                    + "handle go. It needs no click, so it runs on either host.")
                .Result(WakeLockDemo)
        ]);

    private static Section OrientationTopic() =>
        new("orientation", "Orientation", () =>
        [
            P.Class("text-ui-muted")[
                "Read the screen orientation via Screen.Orientation and, for an installed or fullscreen app, ",
                "lock it. Locking is usually rejected outside fullscreen and is often unsupported on desktop."
            ],
            CodeSample
                .Files(["OrientationDemo.cs"])
                .Notes("Screen.Orientation.Type and Angle read it; Lock(OrientationLockType)/Unlock() change it. "
                    + "Lock is WASM-only — it needs the live, usually fullscreen, document. Gate on IsSupported.")
                .Result(OrientationDemo)
        ]);

    private static Section FullscreenTopic() =>
        new("fullscreen", "Fullscreen", () =>
        [
            P.Class("text-ui-muted")[
                "Present an element fullscreen with MDN's Fullscreen API: RequestFullscreen() on its ElementRef, ",
                "Document.ExitFullscreen() to leave. WASM-only: requestFullscreen needs a live user ",
                "gesture. Pairs with Orientation — locking the orientation generally requires fullscreen first."
            ],
            CodeSample
                .Files(["FullscreenDemo.cs"])
                .Notes("await Document.FullscreenElement == _stage says which ref is showing (null for none). Gate on "
                    + "Document.FullscreenEnabled and catch JSException — a request without activation rejects.")
                .Result(FullscreenDemo)
        ]);

    private static Section PictureInPictureTopic() =>
        new("picture-in-picture", "Picture-in-Picture", () =>
        [
            P.Class("text-ui-muted")[
                "Float a video into an always-on-top miniplayer the user keeps visible while they scroll or ",
                "switch tabs, via RequestPictureInPicture() on the video's ElementRef (MDN's Picture-in-Picture API). WASM-only: ",
                "requestPictureInPicture needs a live user gesture. This demo synthesizes its video from an ",
                "animated canvas (sibling scoped JS), so it needs no shipped media file."
            ],
            CodeSample
                .Files(["PictureInPictureDemo.cs", "PictureInPictureDemo.ts"])
                .Notes("RequestPictureInPicture() answers with the kept PictureInPictureWindow; Document.ExitPictureInPicture() "
                    + "brings it back. Gate on Document.PictureInPictureEnabled and catch JSException — a request without "
                    + "activation rejects.")
                .Result(PictureInPictureDemo)
        ]);

    private static Section EyeDropperTopic() =>
        new("eye-dropper", "EyeDropper", () =>
        [
            P.Class("text-ui-muted")[
                "Let the user pick a color from anywhere on screen with the system magnifier loupe, via ",
                "EyeDropper (the EyeDropper API) — handy for a design tool or theme editor. WASM-only: ",
                "open() needs a live user gesture, and it's Chromium-family only at the time of writing."
            ],
            CodeSample
                .Files(["EyeDropperDemo.cs"])
                .Notes("EyeDropper.Create() keeps a picker; Open() resolves with the picked SRGBHex (e.g. \"#3366ff\"). "
                    + "It rejects when the user cancels (Escape) or the browser has no EyeDropper — so try/catch.")
                .Result(EyeDropperDemo)
        ]);

    private static Section IdleDetectionTopic() =>
        new("idle", "Idle detection", () =>
        [
            P.Class("text-ui-muted")[
                "Be notified when the user goes idle (no input for a threshold) or the screen locks, via ",
                "IdleDetector (the Idle Detection API) — e.g. to auto-lock a session, pause a sync, or update ",
                "presence in a collaborative app. WASM-only: the idle-detection permission needs a live gesture ",
                "and the detector needs the live document."
            ],
            CodeSample
                .Files(["IdleDetectorDemo.cs"])
                .Notes("IdleDetector.RequestPermission() must run from a gesture; IdleDetector.Create() keeps a detector, "
                    + "OnChange fires on each user/screen state change, and Start(new() { Threshold = 60_000 }) begins "
                    + "watching — the spec enforces a 60-second minimum. Dispose the detector to stop.")
                .Result(IdleDetectorDemo)
        ]);

    private static Section MediaDevicesTopic() =>
        new("media-devices", "Camera & microphone", () =>
        [
            P.Class("text-ui-muted")[
                "Capture the camera, microphone, or screen and show it in a <video> via MDN's MediaDevices in ",
                "Rask.Web (getUserMedia / getDisplayMedia) — for photo capture, video calls, or screen recording. ",
                "WASM-only: capture needs a live user gesture and a secure context. Stop each track to release ",
                "the hardware (the camera indicator turns off), then dispose of the stream."
            ],
            CodeSample
                .Files(["MediaDevicesDemo.cs"])
                .Notes("Navigator.MediaDevices.GetUserMedia(new() { Video = new() }) asks for the camera — an empty "
                    + "constraints object is MDN's `video: true`, and leaving Video null does not ask. It answers with "
                    + "a kept MediaStream: SetSrcObject on an ElementRef<HTMLVideoElement> shows it, Play() starts it. "
                    + "Gate on IsSupported and catch JSException — a denied request rejects.")
                .Result(MediaDevicesDemo)
        ]);

    private static Section WebSerialTopic() =>
        new("serial", "Web Serial", () =>
        [
            P.Class("text-ui-muted")[
                "Talk to a serial device — an Arduino or microcontroller, a GPS, a USB-to-serial adapter — ",
                "straight from C# via MDN's Web Serial API in Rask.Web: pick a port, write a line, and watch inbound ",
                "bytes stream into the log. WASM-only: requestPort() needs a live user gesture, and it's ",
                "Chromium-family only at the time of writing."
            ],
            CodeSample
                .Files(["SerialDemo.cs"])
                .Notes("await Navigator.Serial.RequestPort() shows the browser port chooser and keeps the SerialPort "
                    + "it answers (dismissing the chooser throws); Open(new() { BaudRate = 9600 }) opens it. "
                    + "port.Readable.GetReader() reads inbound chunks with Read<byte[]>() until Done, and "
                    + "port.Writable.GetWriter() writes a byte[] with Write(bytes). Cancel the reader, Close() the "
                    + "port, then dispose it to release it. Gate on await Navigator.Serial.IsSupported.")
                .Result(SerialDemo)
        ]);

    private static Section WebUsbTopic() =>
        new("usb", "WebUSB", () =>
        [
            P.Class("text-ui-muted")[
                "Pair with and drive a USB device — custom hardware, a dev board, an instrument — straight from C# ",
                "via MDN's WebUSB API in Rask.Web: show its descriptor, open it, claim an interface, and run ",
                "bulk / interrupt / control transfers. WASM-only: requestDevice() needs a live user gesture, and ",
                "it's Chromium-family only at the time of writing."
            ],
            CodeSample
                .Files(["UsbDemo.cs"])
                .Notes("await Navigator.Usb.RequestDevice(new() { Filters = [] }) shows the browser device chooser and "
                    + "keeps the USBDevice it answers (dismissing it throws). Transfer payloads cross as byte[] — "
                    + "TransferOut(endpoint, bytes); dispose the device to release it. Actual transfers are "
                    + "device-specific, so the demo shows discovery + lifecycle. Gate on await Navigator.Usb.IsSupported.")
                .Result(UsbDemo)
        ]);

    private static Section WebHidTopic() =>
        new("hid", "WebHID", () =>
        [
            P.Class("text-ui-muted")[
                "Talk to a human-interface device that no higher-level API covers — a gamepad with custom reports, ",
                "a keyboard with extra keys, simulation controls, point-of-sale hardware — via MDN's WebHID API in ",
                "Rask.Web: open it, send output / feature reports, and subscribe to its live input-report stream. ",
                "WASM-only: requestDevice() needs a live user gesture, and it's Chromium-family only at the time of ",
                "writing."
            ],
            CodeSample
                .Files(["HidDemo.cs"])
                .Notes("await Navigator.Hid.RequestDevice(new() { Filters = [] }) shows the browser chooser and keeps "
                    + "each granted HIDDevice (none if dismissed). Open() a device, then OnInputReport hands each "
                    + "report to your handler, which re-renders the component; dispose the subscription and the "
                    + "device to release. Report payloads cross as byte[]. Gate on await Navigator.Hid.IsSupported.")
                .Result(HidDemo)
        ]);

    private static Section WebBluetoothTopic() =>
        new("bluetooth", "Web Bluetooth", () =>
        [
            P.Class("text-ui-muted")[
                "Pair with a Bluetooth Low Energy device and talk to its GATT services from C# — connect, read / ",
                "write characteristics, and subscribe to notifications (heart-rate monitors, thermometers, fitness ",
                "sensors, custom hardware) — via MDN's Web Bluetooth API in Rask.Web. WASM-only: requestDevice() ",
                "needs a live user gesture, and it's Chromium-family only at the time of writing."
            ],
            CodeSample
                .Files(["BluetoothDemo.cs"])
                .Notes("await Navigator.Bluetooth.RequestDevice(new() { Filters = [new() { Services = "
                    + "[\"battery_service\"] }] }) shows the chooser and keeps the BluetoothDevice (dismissing it "
                    + "throws). Gatt.Connect(), then GetPrimaryService → GetCharacteristic → ReadValue() / "
                    + "WriteValueWithResponse(bytes) / OnCharacteristicValueChanged. This demo reads the standard "
                    + "Battery Service. Values cross as byte[]; Gatt.Disconnect() then dispose the device. Gate on "
                    + "await Navigator.Bluetooth.IsSupported.")
                .Result(BluetoothDemo)
        ]);
}
