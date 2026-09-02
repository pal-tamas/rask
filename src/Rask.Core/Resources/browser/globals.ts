// The C#-facing adapter over ./ — and the ONLY module in this directory with side effects.
//
// Rask's C# wrappers reach the browser by handing IJSRuntime a dotted identifier ("__raskApi.
// geolocation") that the invoke dispatcher resolves against `window` at call time. That is why these
// are globals rather than exports: the caller is .NET, and it resolves names, not modules.
//
// Importing this file registers those namespaces. Both framework clients do exactly that — Server's
// rask.ts and WASM's rask.wasm.ts — while a TypeScript front end imports the modules beside it and
// never loads this file at all.
//
// What lives HERE rather than in a module is everything that belongs to .NET's calling convention
// and not to the browser:
//
//   * positional arguments, because an IJSRuntime call site has no object literals to spare;
//   * numeric ids and the maps that key subscriptions by them, because C# owns the id and a
//     `() => void` cannot cross the interop boundary;
//   * DotNet.invokeMethodAsync callbacks into [JSInvokable] statics.
//
// The keys and signatures below are a contract with the C# wrappers. Renaming one is a silent
// break — the identifier simply fails to resolve at run time, in the browser, with no compiler
// anywhere in the path to notice.

import * as cookies from "./cookies.js";
import * as geolocation from "./geolocation.js";
import * as mediaQuery from "./mediaQuery.js";
import * as networkInformation from "./networkInformation.js";
import * as permissions from "./permissions.js";
import * as screenInfo from "./screen.js";
import * as speechSynthesis from "./speechSynthesis.js";
import * as storageManager from "./storageManager.js";
import * as visualViewport from "./visualViewport.js";

window.__raskApi = window.__raskApi || {
    // IGeolocation.GetCurrentPositionAsync. Rejects when unsupported, denied or timed out; the
    // awaiting ValueTask surfaces that as a JSException.
    geolocation: (
        enableHighAccuracy: boolean,
        timeoutMs: number | null,
        maximumAgeMs: number | null) =>
        geolocation.getCurrentPosition({enableHighAccuracy, timeoutMs, maximumAgeMs}),

    // IPermissions.QueryAsync — the live PermissionStatus flattened to its state string.
    permissionState: (name: PermissionName) => permissions.query(name),

    // ICookies. Positional here, an options object in the module.
    cookieGet: (name: string) => cookies.get(name),
    cookieAll: () => cookies.getAll(),
    cookieSet: (
        name: string,
        value: string,
        maxAge: number | null,
        expires: string | null,
        path: string | null,
        domain: string | null,
        sameSite: string | null,
        secure: boolean) =>
        cookies.set(name, value, {
            maxAgeSeconds: maxAge,
            expires,
            path,
            domain,
            sameSite: sameSite as "Strict" | "Lax" | "None" | null,
            secure
        }),
    cookieDelete: (name: string, path: string | null) => cookies.remove(name, path),

    // IMediaQuery — just the boolean, since MediaQueryList is live and does not serialize.
    matchMedia: (query: string) => mediaQuery.matches(query),

    // IStorageEstimator.
    storageSupported: () => storageManager.isSupported(),
    storageEstimate: () => storageManager.estimate(),
    storagePersisted: () => storageManager.persisted(),
    storagePersist: () => storageManager.persist(),

    // IVisualViewport.
    visualViewportSupported: () => visualViewport.isSupported(),
    visualViewport: () => visualViewport.current(),

    // IScreenInfo.
    screen: () => screenInfo.info(),

    // ISpeechSynthesis.
    speechSupported: () => speechSynthesis.isSupported(),
    speak: (text: string, options?: RaskSpeakOptions | null) =>
        speechSynthesis.speak(text, options || undefined),
    cancelSpeech: () => speechSynthesis.cancel(),

    // INetworkInfo.
    networkSupported: () => networkInformation.isSupported(),
    network: () => networkInformation.current()
};

// IGeolocation.WatchAsync. C# mints the id and holds the subscription, so the stop function the
// module returns is parked here under that id rather than handed back.
window.__raskGeoWatch = window.__raskGeoWatch || (() => {
    const stops = new Map<number, () => void>();
    return {
        watch: (
            id: number,
            enableHighAccuracy: boolean,
            timeoutMs: number | null,
            maximumAgeMs: number | null) => {
            const stop = geolocation.watchPosition(
                (fix) => window.DotNet.invokeMethodAsync("Rask.Core", "RaskGeolocationFix", id, fix),
                {enableHighAccuracy, timeoutMs, maximumAgeMs});
            stops.set(id, stop);
        },
        clear: (id: number) => {
            const stop = stops.get(id);
            if (stop == null) {
                return;
            }
            stops.delete(id);
            stop();
        }
    };
})();
