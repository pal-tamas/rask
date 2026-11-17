// The worker's end of [OffThread] (Rask.Cqrs): a second .NET runtime beside the page — the app's own _framework, created
// here as the .NET blazorwebworker template does — that runs the app's handlers and nothing else. No Main, no DOM, no
// page: .NET's OffThreadHost loads the app's handlers into a container of their own and answers frames, each through
// the `answer` import below.
//
// Started by rask-offthread.ts, which posts {t: "start"} first. Calls that arrive before the runtime is up wait for it.

/// <reference lib="webworker" />

interface MemoryView {
    slice(): Uint8Array;
}

interface OffThreadHost {
    Start(mainAssembly: string, culture: string, baseAddress: string): Promise<void>;
    Handle(id: number, frame: Uint8Array): void;
    Cancel(id: number): void;
}

interface DotnetRuntime {
    getAssemblyExports(assembly: string): Promise<unknown>;
    getConfig(): { mainAssemblyName?: string };
    setModuleImports(name: string, imports: Record<string, unknown>): void;
    INTERNAL?: { loadSatelliteAssemblies?(cultures: string[]): Promise<void> };
}

interface DotnetModule {
    dotnet: { create(): Promise<DotnetRuntime> };
}

const scope = self as unknown as DedicatedWorkerGlobalScope;
let host: Promise<OffThreadHost> | null = null;

// .NET's answer to one request: a view over its memory, copied into a buffer of exactly the frame and transferred.
function answer(id: number, frame: MemoryView): void {
    const buffer = frame.slice().buffer;
    scope.postMessage({t: "answer", id, frame: buffer}, [buffer]);
}

async function boot(dotnetUrl: string, culture: string, base: string): Promise<OffThreadHost> {
    const {dotnet} = await import(/* @vite-ignore */ dotnetUrl) as DotnetModule;
    const runtime = await dotnet.create();
    runtime.setModuleImports("rask-worker", {answer});
    if (culture && runtime.INTERNAL?.loadSatelliteAssemblies) {
        try {
            await runtime.INTERNAL.loadSatelliteAssemblies([culture]);
        } catch {
            // An app without resources for this culture has none to load.
        }
    }
    const exports = await runtime.getAssemblyExports("Rask.Wasm") as { Rask: { Wasm: { OffThreadHost: OffThreadHost } } };
    const offThread = exports.Rask.Wasm.OffThreadHost;
    await offThread.Start(runtime.getConfig().mainAssemblyName ?? "", culture, base);
    return offThread;
}

scope.onmessage = (event: MessageEvent) => {
    const data = event.data as { t: string; id: number; frame: ArrayBuffer; dotnet: string; culture: string; base: string };
    switch (data.t) {
        case "start":
            host = boot(data.dotnet, data.culture, data.base);
            host.catch((error: unknown) => scope.postMessage({t: "failed", reason: String(error)}));
            break;
        case "call":
            host?.then(offThread => offThread.Handle(data.id, new Uint8Array(data.frame)))
                .catch(() => {
                    // Start failed, and that failure is already the page's.
                });
            break;
        case "cancel":
            host?.then(offThread => offThread.Cancel(data.id)).catch(() => undefined);
            break;
    }
};
