// The framework's own `window.__rask*` surface, declared for the runtime's TypeScript.
//
// These are internal to Rask and deliberately NOT in the rask-globals.d.ts that ships to consumers:
// an app has no business calling them, and shipping their shapes would make them API. They exist as
// globals at all because .NET reaches them by dotted name — an IJSRuntime identifier like
// "__raskApi.geolocation" is resolved against `window` at call time — so a module-local binding
// would be invisible to the caller that matters.
//
// Kept narrow on purpose: only what the framework's own modules read or write from another module.

interface Window {
    /** Set by rask-hotreload; called by each host when a hot-reload notification arrives. */
    __raskHotReloadPill?: () => void;

    /** Incremented on every applied hot reload. The watch E2E waits on it rather than sleeping. */
    __raskHotReloadCount?: number;

    /** Guards against the runtime IIFE executing twice in one document (see rask.ts). */
    __raskBooted?: boolean;

    /**
     * An app-supplied hook, called after a script revived by the morph has loaded.
     *
     * Not Rask's own: it is the documented escape hatch for re-initialising a third-party library
     * whose <script> the morph re-inserted, which is why it is optional and why the call site
     * typeof-checks it.
     */
    raskAfterMorph?: () => void;

    /**
     * The form-state guards, WeakMap-backed and hung on `window` so the morph, rask-input and each
     * host runtime all reach the same instance.
     *
     * They predate ES modules here: with the old splice, "which copy do you get" depended on the
     * order MSBuild pasted the files together, and a global was the only thing that could not be
     * duplicated. Real modules make a module-level `const` do the same job properly — worth
     * collapsing, but not in the same change that moves the language.
     *
     * Each maps a control to what the SERVER last rendered for it, so an echoed frame can tell a
     * stale value from a user's edit.
     */
    __raskPendingValues?: WeakMap<Element, string>;
    __raskPendingChecked?: WeakMap<Element, boolean>;
    __raskPendingSelected?: WeakMap<Element, boolean>;

    /**
     * Install-once flags for the two document-level behaviours the diff codec owns — the focus trap
     * and the popover placer. Both bind global listeners and a MutationObserver, so a second install
     * in the same document would double every handler.
     */
    __raskFocusTrap?: boolean;
    __raskPopover?: boolean;
    __raskReload?: boolean;

    /**
     * The legacy IE clipboard object. lib.dom no longer declares it; the one read of it is a fallback
     * inside a try/catch, kept because it costs a line.
     */
    clipboardData?: DataTransfer;

    /** Controls the user has edited, mapped to the attribute state they were rendered with. */
    __raskDirtyFields?: WeakMap<Element, RaskFieldBase>;

    // The transport-neutral PWA helpers (rask-pwa), reached from C# by dotted name.
    __raskPush: RaskPushApi;
    __raskNotify: RaskNotifyApi;
    __raskBadge: RaskBadgeApi;
    __raskWakeLock: RaskWakeLockApi;
}

/** The C# PushSubscription record, as this side shapes it. Field names are the wire contract. */
interface RaskPushSubscriptionDto {
    endpoint: string;
    expirationTime: number | null;
    p256dh: string;
    auth: string;
}

interface RaskPushApi {
    isSupported(): boolean;
    requestPermission(): Promise<NotificationPermission>;
    register(swUrl: string): Promise<void>;
    subscribe(vapidPublicKey: string): Promise<RaskPushSubscriptionDto>;
    getSubscription(): Promise<RaskPushSubscriptionDto | null>;
    unsubscribe(): Promise<boolean>;

    /** Internal helpers, on the object because the methods above reach them through `window`. */
    _serialize(sub: PushSubscription): RaskPushSubscriptionDto;
    _b64url(buf: ArrayBuffer | null): string;
    /**
     * Uint8Array<ArrayBuffer>, not a bare Uint8Array: the type is generic over its backing buffer
     * now, and PushManager.subscribe's applicationServerKey rejects a SharedArrayBuffer-backed one.
     * `new Uint8Array(length)` always allocates a plain ArrayBuffer, so this is a statement of fact
     * rather than a cast.
     */
    _urlB64ToBytes(base64: string): Uint8Array<ArrayBuffer>;
}

interface RaskNotifyApi {
    isSupported(): boolean;
    show(title: string, options?: NotificationOptions): void;
}

interface RaskBadgeApi {
    isSupported(): boolean;
    set(count: number | null | undefined): Promise<void>;
    clear(): Promise<void>;
}

interface RaskWakeLockApi {
    isSupported(): boolean;
    request(): Promise<number>;
    release(id: number): Promise<void>;
}

/**
 * A parent that may support the Atomic Move API (`moveBefore`, Chromium 133+), which lib.dom does
 * not declare yet.
 *
 * Optional, which is what makes a plain `Node` assignable to it and keeps the runtime feature test
 * (`if (parent.moveBefore)`) meaningful rather than a formality: the fallback path is the one most
 * browsers still take.
 */
interface MovableParent extends Node {
    moveBefore?(node: Node, anchor: Node | null): void;
}

/**
 * What the server rendered for one control, read from ATTRIBUTES only.
 *
 * A union rather than a string because the three control families disagree about what "what the
 * server rendered" even is: a checkbox has a boolean, a text input has its value attribute, and
 * `null` is a fourth state meaning the server rendered NO value attribute at all — an uncontrolled
 * input, which morph never writes to. Flattening null into "" would make the restore treat the two
 * as the same thing, which they are not.
 */
type RaskFieldBase = string | boolean | null;

/**
 * The activation-gated capability shims the gesture bridge reaches, published by rask-api and
 * rask-wasm-api.
 *
 * Declared with only the members the bridge calls, and NOT optional: rask-api installs every one of
 * them unconditionally, and every host imports it. What may be missing is the browser API each shim
 * wraps — which is what its own isSupported() reports, and why the shims return null rather than
 * throwing. Making the shim itself optional would force each caller to re-prove something the import
 * already guarantees.
 */
interface Window {
    __raskFullscreen: { request(el: Element | null): Promise<unknown> | null };
    __raskEyeDropper: { open(): Promise<unknown> | null };
    __raskOrientation: { lock(type: string | null): Promise<unknown> | null };
    __raskPip: { request(el: Element | null): Promise<unknown> | null };
    __raskInstall: { prompt(): Promise<string> };
    __raskMedia: {
        /**
         * Resolves the STREAM ID, not a permission string. Ids are minted in JS (`++nextId`), so this
         * is a number — the gesture bridge's `String(id)` is what turns it into the value C# receives.
         */
        getUserMedia(constraints: unknown): Promise<number>;
        attach(id: number, el: Element): unknown;

        /**
         * The id-to-MediaStream mapping other framework helpers deal in: __raskRtc sends a captured
         * stream to a peer, and registers a peer's remote stream so C# gets an id it can attach to a
         * <video>. Not for application use; C# never calls these.
         */
        get(id: number): MediaStream | undefined;
        stop(id: number): void;
        adopt(stream: MediaStream): number;
    };
}

/** What a `data-rask-gesture` attribute carries, as JSON. */
interface RaskGestureSpec {
    /** Which capability to run, keyed into the gesture table. */
    cap: string;
    /** Optional result-callback id; when set, the resolved value is posted back to C#. */
    rid?: string | number | null;
    /** The capability's optional string argument (orientation type, JSON media constraints). */
    arg?: string | null;
    /** An optional target element, named by the ElementRef id the ref reviver also resolves. */
    el?: string | null;
}

interface Window {
    /**
     * The framework-internal browser shims that rask-api and rask-wasm-api publish.
     *
     * A template-literal index signature rather than thirty hand-written interfaces, and the reason
     * is where the contract lives: each of these is reached from C# by a dotted IJSRuntime identifier
     * (`"__raskApi.geolocation"`), resolved against `window` at call time, and the authoritative
     * shape is the C# wrapper that calls it. A second copy of those thirty shapes here would be a
     * copy that drifts, and drift is exactly what this migration exists to remove.
     *
     * The handful that ARE read from another module — the gesture bridge's capabilities, the PWA
     * helpers — are declared explicitly above. An explicit member always wins over the index
     * signature, so those stay precisely typed; this only covers the ones whose only caller is .NET.
     */
    [key: `__rask${string}`]: unknown;
}

/**
 * The vendor-prefixed Network Information object. lib.dom declares none of the three, and the
 * effective type / downlink / rtt fields are what IIsNetworkStatus maps.
 */
interface NetworkInformationLike {
    effectiveType?: string;
    downlink?: number;
    rtt?: number;
    saveData?: boolean;
}

interface Navigator {
    connection?: NetworkInformationLike;
    mozConnection?: NetworkInformationLike;
    webkitConnection?: NetworkInformationLike;
}

/** The options ISpeechSynthesis passes to `speak`. */
interface RaskSpeakOptions {
    lang?: string;
    rate?: number;
    pitch?: number;
    volume?: number;
}

/** The Battery Status manager. lib.dom dropped it; Chromium still ships getBattery(). */
interface BatteryManagerLike extends EventTarget {
    level: number;
    charging: boolean;
    chargingTime: number;
    dischargingTime: number;
}

interface Navigator {
    getBattery?(): Promise<BatteryManagerLike>;
}

/**
 * Web Speech recognition, which lib.dom does not declare — it is Chromium-family only and still
 * vendor-prefixed. Only what the shim drives is described.
 */
interface SpeechRecognitionLike {
    lang: string;
    continuous: boolean;
    interimResults: boolean;
    onresult: ((e: SpeechRecognitionEventLike) => void) | null;
    onerror: ((e: { error?: string }) => void) | null;
    onend: (() => void) | null;
    start(): void;
    stop(): void;
}

interface SpeechRecognitionAlternativeLike {
    transcript: string;
    confidence: number;
}

interface SpeechRecognitionResultLike {
    readonly length: number;
    isFinal: boolean;
    [index: number]: SpeechRecognitionAlternativeLike;
}

interface SpeechRecognitionEventLike {
    resultIndex: number;
    results: { readonly length: number;[index: number]: SpeechRecognitionResultLike };
}

interface Window {
    SpeechRecognition?: { new(): SpeechRecognitionLike };
    webkitSpeechRecognition?: { new(): SpeechRecognitionLike };
}

/** The options ISpeechRecognition passes to `start`. */
interface RaskSpeechOptions {
    lang?: string;
    continuous?: boolean;
    interimResults?: boolean;
}

/**
 * The iOS permission gate on the two motion-sensor event constructors. Safari added
 * `requestPermission` as a static on each; lib.dom declares neither, and the `typeof … === "function"`
 * test at both call sites is what keeps the non-iOS path working.
 */
interface MotionPermissionCtor {
    requestPermission?(): Promise<string>;
}

interface Window {
    DeviceOrientationEvent?: MotionPermissionCtor;
    DeviceMotionEvent?: MotionPermissionCtor;
}

/** The File System Access picker options this shim accepts from C#. */
interface RaskFilePickerOptions {
    description?: string;
    accept?: Record<string, string[]>;
    suggestedName?: string;
}

interface Window {
    showOpenFilePicker?(options?: unknown): Promise<FileSystemFileHandle[]>;
    showSaveFilePicker?(options?: unknown): Promise<FileSystemFileHandle>;
    showDirectoryPicker?(options?: unknown): Promise<FileSystemDirectoryHandle>;
}

/** The metadata IMediaSession passes across, matching MediaMetadataInit's shape. */
interface RaskMediaMetadata {
    title?: string;
    artist?: string;
    album?: string;
    artwork?: MediaImage[];
}

/**
 * The WebAuthn options C# hands across, with every ArrayBuffer field carried as base64url text —
 * the one encoding that marshals identically on every host. The shim decodes them before calling
 * navigator.credentials, which is why these are strings here and buffers there.
 */
interface RaskCredentialDescriptor {
    type?: string;
    id: string;
    transports?: string[];
}

interface RaskWebAuthnCreateOptions {
    challenge: string;
    rp: PublicKeyCredentialRpEntity;
    user: { id: string; name: string; displayName: string };
    pubKeyCredParams?: PublicKeyCredentialParameters[];
    timeoutMs?: number;
    attestation?: AttestationConveyancePreference;
    authenticatorSelection?: AuthenticatorSelectionCriteria;
    excludeCredentials?: RaskCredentialDescriptor[];
}

interface RaskWebAuthnGetOptions {
    challenge: string;
    rpId?: string;
    timeoutMs?: number;
    userVerification?: UserVerificationRequirement;
    allowCredentials?: RaskCredentialDescriptor[];
}

/** One entry of a Web Locks query snapshot, as this shim reshapes it for C#. */
interface RaskLockInfo {
    name?: string;
    mode?: string;
    clientId?: string;
    held: boolean;
}

/** One buffered data-channel message, in the shape the flush hands to C#. */
interface RaskRtcMessage {
    text: string | null;
    data: string | null;
}

/** One live peer connection, plus the buffering the flush needs. */
interface RaskRtcConn {
    pc: RTCPeerConnection;
    ice: RTCIceCandidateInit[];
    timer: ReturnType<typeof setTimeout> | 0;
    /** peer stream id -> the __raskMedia id minted for it, so a repeat ontrack does not duplicate. */
    remote: Map<string, number>;
    /** What AddStream added, so RemoveStream can take exactly those tracks back off. */
    senders: Map<number, RTCRtpSender[]>;
}

/** One live data channel, plus the buffer that holds messages until C# is listening. */
interface RaskRtcChan {
    ch: RTCDataChannel;
    connId: number;
    buf: RaskRtcMessage[];
    dropped: number;
    timer: ReturnType<typeof setTimeout> | 0;
    listening: boolean;
}

/** The peer-connection configuration C# hands across. */
interface RaskRtcConfig {
    iceServers?: string[];
    iceTransportPolicy?: RTCIceTransportPolicy;
}

/** A session description as it crosses the boundary. */
interface RaskRtcDescription {
    type: RTCSdpType;
    sdp: string;
}

/** The data-channel options C# hands across. */
interface RaskRtcChannelOptions {
    ordered?: boolean;
    maxRetransmits?: number;
    protocol?: string;
}

/** The EyeDropper picker. lib.dom does not declare it; it is Chromium-family only. */
interface EyeDropperLike {
    open(): Promise<{ sRGBHex: string }>;
}

declare var EyeDropper: { new(): EyeDropperLike } | undefined;

/** The beforeinstallprompt event, which lib.dom does not declare. */
interface BeforeInstallPromptEventLike extends Event {
    prompt(): void;
    userChoice: Promise<{ outcome: string }>;
}

interface Navigator {
    /** iOS Safari's standalone flag — the only way to detect an installed PWA there. */
    standalone?: boolean;
}

/** The view-transition shim, which reads and writes its own `enabled` flag through window. */
interface RaskViewTransitionApi {
    enabled: boolean;
    supported(): boolean;
    reducedMotion(): boolean;
    set(on: boolean): boolean;
    active(): boolean;
    run(commit: () => void): unknown;
}

interface Window {
    __raskVt: RaskViewTransitionApi;
}

/** The animation options C# hands across. -1 is the wire spelling of Infinity. */
interface RaskAnimOptions {
    durationMs?: number;
    delayMs?: number;
    iterations?: number;
    easing?: string;
    fill?: FillMode;
    direction?: PlaybackDirection;
}

/** The media constraints C# hands `getUserMedia`, flattened rather than nested. */
interface RaskMediaConstraints {
    audio?: boolean;
    video?: boolean;
    facingMode?: string;
}

/**
 * One render frame as it arrives from .NET — over the WebSocket on the Server host, and through the
 * payload buffer on WASM. Both hosts read the same fields, which is why this is declared once.
 *
 * `ops` is deliberately loose: the diff codec owns that shape (see DiffOp in rask-dom.ts), and all
 * the hosts do with it is hand it to applyDiff.
 */
interface RaskFrameReply {
    /** Calls the frame wants run after the DOM is patched. */
    jsInvokes?: RaskFrameJsInvoke[];
    /**
     * The Server transport multiplexes control frames over the same socket as renders, so a frame
     * names its own kind here — "hotReload", "shutdown", "sessionExpired" and so on.
     */
    type?: string;
    /** An auth ticket the client must redeem before it can reconnect. Server transport only. */
    auth?: { ticket?: string; url?: string };
    /** Set on a frame completing a [JSInvokable] call, naming the call it answers. */
    callId?: string;
    success?: boolean;
    result?: unknown;
    error?: string;
    /** Hot-reload frames report whether the edit applied. */
    status?: string;
    /** "diff" selects the op-stream path; anything else is a full-HTML frame. */
    kind?: string;
    ops?: unknown[];
    /** Attribute names the server interned because they repeat within one payload. */
    names?: string[];
    /** Full-HTML frames carry the document; a diff frame may carry just a <head> fragment. */
    html?: string;
    head?: string;
    /** A development fault the app survived, riding the payload rather than a frame of its own. */
    devError?: unknown;
    /**
     * A file the app asked the browser to save. Bytes arrive one of three ways: a token the client
     * pulls from .NET (no base64 inflation), inline base64, or an already-decoded array.
     */
    download?: {
        filename?: string;
        /** The Server host streams the file from a URL rather than carrying its bytes. */
        url?: string;
        token?: string;
        base64?: string;
        bytes?: number[];
        contentType?: string;
    };
    /** A navigation the render performed, so the client can update history and scroll. */
    history?: { url?: string; replace?: boolean; scroll?: string | null; action?: string };
}

/** One entry of a frame's jsInvokes list, as both hosts read it. */
interface RaskFrameJsInvoke {
    id: string | number;
    identifier: string;
    argsJson?: string;
    resultType?: number;
    targetInstanceId?: number;
}

/**
 * One field the redeploy restore re-applies, as saved into sessionStorage.
 *
 * Single-letter keys because this is written to sessionStorage under a size budget
 * (RESTORE_MAX_TOTAL_CHARS): the field names would otherwise cost more than several of the values
 * they label.
 */
interface RaskRestoreField {
    /** The resolve key — a name or id that finds the control again after the reload. */
    k: string;
    /** The control family: "value", "checked" or "radio". */
    t: string;
    /** What the server had rendered, so a replacement that renders the same is not overwritten. */
    b: unknown;
    /** What the user had typed or picked. */
    v: unknown;
}

/** One JS invoke parked until its scoped bundle has loaded. Shared by both hosts. */
interface RaskPendingInvoke {
    taskId: string;
    identifier: string;
    argsJson: string | null;
    resultType: number;
    targetInstanceId: string;
}
