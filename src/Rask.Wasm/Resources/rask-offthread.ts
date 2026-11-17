// The page's end of [OffThread] (Rask.Cqrs): one Web Worker running a second .NET runtime, started the first time a
// message is sent to it. .NET imports this module lazily — JSHost.ImportAsync("rask.offthread", "../rask-offthread.js")
// — so a page that never sends one never downloads it.
//
// Frames are ArrayBuffers in both directions, transferred rather than copied: the encoding is .NET's
// (Rask.Cqrs/OffThreading/OffThreadFrame.cs), and this module only carries them. An answer goes back into .NET through
// the BrowserOffThreadChannel exports.

interface MemoryView {
    slice(): Uint8Array;
}

interface ChannelExports {
    Rask: { Wasm: { BrowserOffThreadChannel: { Answered(id: number, frame: Uint8Array): void; Stopped(reason: string): void } } };
}

declare global {
    // The .NET runtime's own accessor for a started runtime (dotnet.js).
    var getDotnetRuntime: ((id: number) => { getAssemblyExports(assembly: string): Promise<unknown> } | undefined) | undefined;
}

let worker: Worker | null = null;
let channel: Promise<ChannelExports["Rask"]["Wasm"]["BrowserOffThreadChannel"]> | null = null;

function exports(): Promise<ChannelExports["Rask"]["Wasm"]["BrowserOffThreadChannel"]> {
    channel ??= (async () => {
        const runtime = globalThis.getDotnetRuntime?.(0);
        if (!runtime) throw new Error("The page's .NET runtime is not running.");
        return (await runtime.getAssemblyExports("Rask.Wasm") as ChannelExports).Rask.Wasm.BrowserOffThreadChannel;
    })();
    return channel;
}

// The worker loads the page's own runtime, through the page's import map: a worker has no import map of its own, and
// the runtime's file name carries a fingerprint only the map knows.
function dotnetUrl(): string {
    const plain = new URL("./_framework/dotnet.js", import.meta.url).href;
    try {
        return typeof import.meta.resolve === "function" ? import.meta.resolve(plain) : plain;
    } catch {
        return plain;
    }
}

function stop(reason: string): void {
    if (worker) {
        worker.terminate();
        worker = null;
    }
    void exports().then(dotnet => dotnet.Stopped(reason));
}

function start(culture: string): Worker {
    if (worker) return worker;
    const started = new Worker(new URL("./rask-worker.js", import.meta.url), {type: "module", name: "rask-offthread"});
    started.onmessage = (event: MessageEvent) => {
        const data = event.data as { t: string; id?: number; frame?: ArrayBuffer; reason?: string };
        if (data.t === "answer" && data.id !== undefined && data.frame) {
            const id = data.id;
            const frame = new Uint8Array(data.frame);
            void exports().then(dotnet => dotnet.Answered(id, frame));
        } else if (data.t === "failed") {
            stop(`The off-thread worker could not start: ${data.reason ?? "unknown error"}`);
        }
    };
    started.onerror = (event: ErrorEvent) => {
        event.preventDefault();
        stop(`The off-thread worker stopped: ${event.message || "unknown error"}`);
    };
    started.postMessage({t: "start", dotnet: dotnetUrl(), culture, base: document.baseURI});
    worker = started;
    return started;
}

/** Starts the worker now rather than at the first message (`c.OffThread.Warm`). */
export function warm(culture: string): void {
    start(culture);
}

/** Sends one request frame; the answer comes back through `Answered`. */
export function call(id: number, frame: MemoryView, culture: string): void {
    // Copied out at once: the view is over .NET memory, valid only during this call.
    const bytes = frame.slice();
    start(culture).postMessage({t: "call", id, frame: bytes.buffer}, [bytes.buffer]);
}

/** Asks the worker to cancel one request's Current.Cancellation. */
export function cancel(id: number): void {
    worker?.postMessage({t: "cancel", id});
}

/** Stops the worker — a cancelled handler that would not stop. .NET fails what still waits on it. */
export function restart(): void {
    if (worker) {
        worker.terminate();
        worker = null;
    }
}
