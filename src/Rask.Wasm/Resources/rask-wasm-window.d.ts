// The browser APIs the WASM-only device shims drive, which lib.dom does not declare.
//
// Web Serial, WebUSB, Web Bluetooth, the Idle Detection API and the two Background Sync registrations
// are all Chromium-family and outside the standard lib. Declared here rather than in Rask.Core's
// rask-window.d.ts because they are reachable only from the WASM host — the Server client has no
// [JSImport] surface for them, and putting them in Core would advertise a capability that host does
// not have.
//
// Narrow on purpose: every member below is one the shims in rask-wasm-api.ts actually call. A fuller
// transcription of four draft specs would be a second copy to keep in step with browsers, for no
// benefit — these files are the only callers.

// ---- Web Serial ----------------------------------------------------------

interface SerialPortInfoLike {
    usbVendorId?: number;
    usbProductId?: number;
}

interface SerialPortLike {
    readable: ReadableStream<Uint8Array> | null;
    writable: WritableStream<Uint8Array> | null;
    open(options: { baudRate: number;[key: string]: unknown }): Promise<void>;
    close(): Promise<void>;
    getInfo(): SerialPortInfoLike;
}

interface SerialLike {
    requestPort(options?: { filters?: unknown[] }): Promise<SerialPortLike>;
    getPorts(): Promise<SerialPortLike[]>;
}

// ---- WebUSB --------------------------------------------------------------

interface USBTransferResultLike {
    data?: DataView;
    status?: string;
    bytesWritten?: number;
}

interface USBDeviceLike {
    vendorId: number;
    productId: number;
    productName?: string;
    manufacturerName?: string;
    serialNumber?: string;
    opened: boolean;
    open(): Promise<void>;
    close(): Promise<void>;
    selectConfiguration(configurationValue: number): Promise<void>;
    claimInterface(interfaceNumber: number): Promise<void>;
    releaseInterface(interfaceNumber: number): Promise<void>;
    transferIn(endpointNumber: number, length: number): Promise<USBTransferResultLike>;
    transferOut(endpointNumber: number, data: BufferSource): Promise<USBTransferResultLike>;
    controlTransferIn(setup: unknown, length: number): Promise<USBTransferResultLike>;
    controlTransferOut(setup: unknown, data?: BufferSource): Promise<USBTransferResultLike>;
}

interface USBLike extends EventTarget {
    requestDevice(options: { filters: unknown[] }): Promise<USBDeviceLike>;
    getDevices(): Promise<USBDeviceLike[]>;
}

// ---- Web Bluetooth -------------------------------------------------------

interface BluetoothCharacteristicLike extends EventTarget {
    value?: DataView;
    readValue(): Promise<DataView>;
    writeValue(data: BufferSource): Promise<void>;
    writeValueWithResponse(data: BufferSource): Promise<void>;
    writeValueWithoutResponse(data: BufferSource): Promise<void>;
    startNotifications(): Promise<BluetoothCharacteristicLike>;
    stopNotifications(): Promise<BluetoothCharacteristicLike>;
}

interface BluetoothServiceLike {
    getCharacteristic(uuid: string): Promise<BluetoothCharacteristicLike>;
}

interface BluetoothGattLike {
    connected: boolean;
    connect(): Promise<BluetoothGattLike>;
    disconnect(): void;
    getPrimaryService(uuid: string): Promise<BluetoothServiceLike>;
}

interface BluetoothDeviceLike extends EventTarget {
    id: string;
    name?: string;
    gatt?: BluetoothGattLike;
}

interface BluetoothLike {
    requestDevice(options: unknown): Promise<BluetoothDeviceLike>;
    getDevices?(): Promise<BluetoothDeviceLike[]>;
}

// ---- Idle Detection ------------------------------------------------------

interface IdleDetectorLike extends EventTarget {
    userState: string | null;
    screenState: string | null;
    start(options: { threshold: number; signal?: AbortSignal }): Promise<void>;
}

declare var IdleDetector: {
    new(): IdleDetectorLike;
    requestPermission(): Promise<string>;
} | undefined;

// ---- Background Sync -----------------------------------------------------

interface SyncManagerLike {
    register(tag: string): Promise<void>;
    getTags(): Promise<string[]>;
}

interface PeriodicSyncManagerLike {
    register(tag: string, options?: { minInterval?: number }): Promise<void>;
    unregister(tag: string): Promise<void>;
    getTags(): Promise<string[]>;
}

interface ServiceWorkerRegistration {
    sync?: SyncManagerLike;
    periodicSync?: PeriodicSyncManagerLike;
}

interface Navigator {
    serial?: SerialLike;
    usb?: USBLike;
    bluetooth?: BluetoothLike;
}

interface Window {
    /**
     * Raised by the WASM runtime when it prepared a takeover instead of painting: another runtime is
     * still driving this document, so a start that never rendered is correct rather than a hang.
     *
     * Read by Browser/main.ts alongside `__raskPainted` — without it, every takeover boot would
     * report a boot failure over a working page.
     */
    __raskPrepared?: boolean;

    /**
     * Which runtime currently owns the document. Set by the server runtime when it attaches; its
     * presence tells a booting browser runtime it is arriving into a page it must not paint over.
     */
    __raskOwner?: string;

    /** Published by publishPaint(): what a live server runtime calls to hand over the page. */
    __raskWasmPaint?: (url?: string | null) => Promise<void> | void;

    IdleDetector?: typeof IdleDetector;
}

/** One open serial port, plus the read loop and write serialization the shim owns. */
interface RaskSerialEntry {
    port: SerialPortLike;
    reader: ReadableStreamDefaultReader<Uint8Array> | null;
    loop: Promise<void> | null;
    closing: boolean;
    writeChain: Promise<void>;
}

/** The serial open options C# hands across. */
interface RaskSerialOptions {
    filters?: { usbVendorId?: number; usbProductId?: number }[];
    baudRate: number;
    dataBits?: number;
    stopBits?: number;
    parity?: string;
    bufferSize?: number;
    flowControl?: string;
}

/** A web app manifest, as far as the injector rewrites it. */
interface RaskManifest {
    start_url?: string;
    scope?: string;
    theme_color?: string;
    icons?: { src?: string }[];
    screenshots?: { src?: string }[];
    shortcuts?: { url?: string; icons?: { src?: string }[] }[];
    /** Web Share Target: its action is a URL the injector makes absolute like the others. */
    share_target?: { action?: string };
    /** File Handling: each entry's action is likewise a URL. */
    file_handlers?: { action?: string }[];
}

/** The Bluetooth chooser options C# hands across. */
interface RaskBluetoothOptions {
    filters?: unknown[];
    acceptAllDevices?: boolean;
    optionalServices?: string[];
}

/** What requestDevice is finally called with, once the shim has chosen a shape. */
interface RaskBluetoothRequest {
    acceptAllDevices?: boolean;
    filters?: unknown[];
    optionalServices?: string[];
}

/** The connect/disconnect events WebUSB fires, which lib.dom does not declare. */
interface USBConnectionEventLike extends Event {
    device: USBDeviceLike;
}

// ---- WebHID ---------------------------------------------------------------
//
// Not in lib.dom either, despite WebUSB and WebHID often being assumed to travel together.

interface HIDDevice extends EventTarget {
    vendorId: number;
    productId: number;
    productName?: string;
    opened: boolean;
    open(): Promise<void>;
    close(): Promise<void>;
    sendReport(reportId: number, data: BufferSource): Promise<void>;
    sendFeatureReport(reportId: number, data: BufferSource): Promise<void>;
    receiveFeatureReport(reportId: number): Promise<DataView>;
}

interface HIDDeviceFilter {
    vendorId?: number;
    productId?: number;
    usagePage?: number;
    usage?: number;
}

interface HIDConnectionEvent extends Event {
    device: HIDDevice;
}

interface HIDInputReportEvent extends Event {
    device: HIDDevice;
    reportId: number;
    data: DataView;
}

interface HIDLike extends EventTarget {
    requestDevice(options: { filters: HIDDeviceFilter[] }): Promise<HIDDevice[]>;
    getDevices(): Promise<HIDDevice[]>;
}

interface Navigator {
    hid?: HIDLike;
}

// ---- The .NET WASM runtime's boot module ----------------------------------
//
// `./_framework/dotnet.js` is emitted by the WASM SDK into the published bundle; it does not exist
// in the source tree, so the import cannot be resolved at type-check time. Declared with only what
// the bootstrap calls.

declare module "*/dotnet.js" {
    interface DotnetHostBuilder {
        withApplicationArgumentsFromQuery(): DotnetHostBuilder;
        withModuleConfig(config: {
            onDownloadResourceProgress?(loaded: number, total: number): void;
        }): DotnetHostBuilder;
        create(): Promise<{
            getAssemblyExports(name: string): Promise<Record<string, never>>;
            runMain(): Promise<number>;
        }>;
    }

    export const dotnet: DotnetHostBuilder;
}

interface Window {
    /**
     * Set by the WASM runtime the first time it applies a frame.
     *
     * The boot check asks the render path, not the DOM: the obvious test — "is the splash element
     * still in the document" — is wrong, because the morph patches the document in place and leaves
     * it connected, so it reported a boot failure for every successful boot.
     */
    __raskPainted?: boolean;

    /**
     * The boot-failure reporter, published before the first await so a failure inside
     * `dotnet.create()` can already reach it — and so the managed side and rask.wasm share one
     * implementation rather than each growing half of one.
     */
    __raskBootFailed?: (message: string, detail?: string) => void;
}

/**
 * The assembly exports the .NET runtime hands the client at boot, reached through
 * `dotnetExports.Rask.Wasm.JSInterop`.
 *
 * Every member is optional and every call site tests for it, which is not defensiveness: the exports
 * arrive asynchronously, and a frame can be dispatched before the assembly the JSExports live in has
 * finished loading.
 */
interface RaskWasmExports {
    Rask?: {
        Wasm?: {
            JSInterop?: {
                BeginDotNetInvoke(callId: string, assembly: string, method: string, kind: number, argsJson: string): void;
                EndInvokeJS?(taskId: string, succeeded: boolean, resultJson: string): void;
                PullDownload?(token: string): Uint8Array;
                Dispatch?(payload: Uint8Array): Promise<void> | void;
                StopHostedServices?(): void;
                EndInvokeJSResult?(payloadJson: string): void;

                /** The takeover: hand this runtime the page the server runtime was driving. */
                Paint?(url: string | null): Promise<void> | void;
            };
        };
    };
}
