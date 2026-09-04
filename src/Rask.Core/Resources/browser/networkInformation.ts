// Network Information — navigator.connection.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
//
// Still vendor-prefixed in the wild and absent entirely from Firefox and Safari, so every caller has
// to handle null. Treat what it reports as a hint for adapting payload size, never as a fact.

export interface NetworkStatus {
    /** "slow-2g" | "2g" | "3g" | "4g", as the browser estimates it. Null when it declines to say. */
    effectiveType: string | null;
    /** Estimated downlink, megabits per second. */
    downlink: number;
    /** Estimated round-trip time, milliseconds. */
    rtt: number;
    /** The user asked for reduced data use. Honour it. */
    saveData: boolean;
}

/** Still vendor-prefixed, and absent from lib.dom, so the three spellings are named here. */
interface NavigatorWithConnection extends Navigator {
    connection?: NetworkInformationLike;
    mozConnection?: NetworkInformationLike;
    webkitConnection?: NetworkInformationLike;
}

function connection(): NetworkInformationLike | null {
    if (typeof navigator === "undefined") {
        return null;
    }
    const nav = navigator as NavigatorWithConnection;
    return nav.connection || nav.mozConnection || nav.webkitConnection || null;
}

/** The shape the prefixed objects share; the platform lib has no single type for all three. */
interface NetworkInformationLike {
    effectiveType?: string;
    downlink?: number;
    rtt?: number;
    saveData?: boolean;
}

export function isSupported(): boolean {
    return !!connection();
}

/** A plain snapshot of the live connection object, or null where unsupported. */
export function current(): NetworkStatus | null {
    const c = connection();
    if (!c) {
        return null;
    }
    return {
        effectiveType: c.effectiveType || null,
        downlink: typeof c.downlink === "number" ? c.downlink : 0,
        rtt: typeof c.rtt === "number" ? c.rtt : 0,
        saveData: !!c.saveData
    };
}
