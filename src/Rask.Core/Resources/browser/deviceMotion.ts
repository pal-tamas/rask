// Device motion — the "devicemotion" event.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
// Permission works the same way as ./deviceOrientation.ts, and on iOS both need a user gesture.

import type {SensorOptions} from "./deviceOrientation.js";

export type {SensorOptions};

/** See ./deviceOrientation.ts — the same iOS gate, declared locally for the same reason. */
interface PermissionGatedEvent {
    requestPermission?(): Promise<string>;
}

export interface MotionReading {
    /** Acceleration excluding gravity, m/s². Undefined where the device has no such sensor. */
    accelerationX: number | null | undefined;
    accelerationY: number | null | undefined;
    accelerationZ: number | null | undefined;
    /** Rotation rate, degrees per second. */
    rotationAlpha: number | null | undefined;
    rotationBeta: number | null | undefined;
    rotationGamma: number | null | undefined;
    /** Milliseconds between readings, as the hardware reports it. */
    interval: number;
}

export function isSupported(): boolean {
    return typeof window !== "undefined" && "DeviceMotionEvent" in window;
}

/** Ask for permission. See ./deviceOrientation.ts — the iOS gesture requirement is the same. */
export function requestPermission(): Promise<string> {
    // Bracket access, not `.DeviceMotionEvent`. Angular's scaffolded tsconfig turns on
    // noPropertyAccessFromIndexSignature, which rejects dot-access into an index signature — so the
    // dotted form compiles everywhere except the one framework whose CLI writes the strictest config,
    // and fails there as an npm build error nobody reads back to this line.
    const evt = typeof window === "undefined"
        ? undefined
        : (window as unknown as Record<string, PermissionGatedEvent | undefined>)["DeviceMotionEvent"];
    if (!evt) {
        return Promise.resolve("denied");
    }
    if (typeof evt.requestPermission === "function") {
        return evt.requestPermission().catch(() => "denied");
    }
    return Promise.resolve("granted");
}

/** Watch the device's motion. Returns the stop function. */
export function watch(
    onReading: (reading: MotionReading) => void,
    options?: SensorOptions): () => void {
    const throttleMs = (options && options.throttleMs) || 0;
    let last = 0;

    const handler = (e: DeviceMotionEvent) => {
        if (throttleMs > 0) {
            const now = Date.now();
            if (now - last < throttleMs) {
                return;
            }
            last = now;
        }
        // Both are nullable on the event; the empty fallback keeps every read below defined without
        // inventing zeroes the sensor never reported.
        const a: Partial<DeviceMotionEventAcceleration> = e.acceleration ?? {};
        const r: Partial<DeviceMotionEventRotationRate> = e.rotationRate ?? {};
        onReading({
            accelerationX: a.x,
            accelerationY: a.y,
            accelerationZ: a.z,
            rotationAlpha: r.alpha,
            rotationBeta: r.beta,
            rotationGamma: r.gamma,
            interval: e.interval
        });
    };

    window.addEventListener("devicemotion", handler as EventListener);

    let stopped = false;
    return () => {
        if (stopped) {
            return;
        }
        stopped = true;
        window.removeEventListener("devicemotion", handler as EventListener);
    };
}
