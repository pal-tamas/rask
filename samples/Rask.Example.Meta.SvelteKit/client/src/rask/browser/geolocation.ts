// Geolocation — navigator.geolocation.
//
// Part of Rask's shared browser layer: ordinary TypeScript modules consumed BOTH by the framework's
// own clients (Server and WASM, through ./globals.ts) and directly by a TypeScript front end that
// imports them. Two rules hold for every module in this directory:
//
//   1. No side effects at import time, and no `window` access outside a function body. A module here
//      is imported by a Next/Nuxt SERVER render, where there is no window at all, and by a bundler
//      that can only tree-shake what it can prove inert.
//   2. Where the platform already has a name, keep the platform's name. `getCurrentPosition` and
//      `watchPosition` read the same here as they do in lib.dom, so existing knowledge transfers.
//
// The C# calling convention — numeric ids, DotNet.invokeMethodAsync callbacks, positional arguments —
// lives in ./globals.ts, not here. A subscription in this file returns a stop function, which is what
// TypeScript expects; globals.ts is what turns that into an id-keyed map for IJSRuntime.

/** A single position fix, flattened from GeolocationPosition into something serializable. */
export interface GeolocationFix {
    latitude: number;
    longitude: number;
    accuracy: number;
    altitude: number | null;
    altitudeAccuracy: number | null;
    heading: number | null;
    speed: number | null;
    timestampMs: number;
}

export interface GeolocationOptions {
    enableHighAccuracy?: boolean;
    /** Milliseconds before the request fails. Omitted means the browser's own default (no timeout). */
    timeoutMs?: number | null;
    maximumAgeMs?: number | null;
}

/**
 * GeolocationPosition holds live `coords`; flatten it so the value survives both a structured
 * serialization to C# and an ordinary `JSON.stringify` in a front end.
 */
function toFix(position: GeolocationPosition): GeolocationFix {
    const c = position.coords;
    return {
        latitude: c.latitude,
        longitude: c.longitude,
        accuracy: c.accuracy,
        altitude: c.altitude,
        altitudeAccuracy: c.altitudeAccuracy,
        heading: c.heading,
        speed: c.speed,
        timestampMs: position.timestamp
    };
}

function toPositionOptions(options?: GeolocationOptions): PositionOptions {
    const opts: PositionOptions = {
        enableHighAccuracy: !!(options && options.enableHighAccuracy),
        maximumAge: (options && options.maximumAgeMs) || 0
    };
    if (options && options.timeoutMs != null) {
        opts.timeout = options.timeoutMs;
    }
    return opts;
}

/** Whether this browser exposes the Geolocation API at all. */
export function isSupported(): boolean {
    return typeof navigator !== "undefined" && !!navigator.geolocation;
}

/**
 * One fix. Rejects when geolocation is unsupported, the user denies permission, or the request times
 * out — the browser's own error message is preserved where it has one.
 */
export function getCurrentPosition(options?: GeolocationOptions): Promise<GeolocationFix> {
    return new Promise<GeolocationFix>((resolve, reject) => {
        if (!isSupported()) {
            reject(new Error("Geolocation is not supported in this browser."));
            return;
        }
        navigator.geolocation.getCurrentPosition(
            (pos) => resolve(toFix(pos)),
            (err: GeolocationPositionError) =>
                reject(new Error((err && err.message) || ("Geolocation error " + (err && err.code)))),
            toPositionOptions(options));
    });
}

/**
 * Live tracking. Returns the stop function; calling it clears the underlying watch, and calling it
 * more than once is harmless.
 *
 * Errors are swallowed deliberately, matching the framework's long-standing behaviour: a watch that
 * cannot get a fix right now (tunnel, temporary denial) should keep watching rather than tear itself
 * down, and there is no error channel on a subscription that a caller could act on anyway.
 */
export function watchPosition(
    onFix: (fix: GeolocationFix) => void,
    options?: GeolocationOptions): () => void {
    if (!isSupported()) {
        return () => { /* nothing was started */ };
    }

    const watchId = navigator.geolocation.watchPosition(
        (pos) => onFix(toFix(pos)),
        () => { /* keep watching, surface nothing */ },
        toPositionOptions(options));

    let stopped = false;
    return () => {
        if (stopped) {
            return;
        }
        stopped = true;
        navigator.geolocation.clearWatch(watchId);
    };
}
