// Storage quota and persistence — navigator.storage (StorageManager).
//
// See ./geolocation.ts for the two rules every module in this directory follows.
//
// Named for the platform interface (StorageManager) rather than "storage", which in a browser context
// far more often means localStorage — that one is ./webStorage.ts.

export interface StorageEstimate {
    /** Bytes the origin may use. 0 when the browser declines to say. */
    quota: number;
    /** Bytes the origin currently uses, as the browser accounts for it. */
    usage: number;
}

export function isSupported(): boolean {
    return typeof navigator !== "undefined" && !!(navigator.storage && navigator.storage.estimate);
}

/** Quota and usage, or null where the API is unavailable. Both figures are approximate by design. */
export async function estimate(): Promise<StorageEstimate | null> {
    if (!isSupported()) {
        return null;
    }
    const e = await navigator.storage.estimate();
    return {quota: e.quota || 0, usage: e.usage || 0};
}

/** Whether this origin's storage is already exempt from eviction under storage pressure. */
export async function persisted(): Promise<boolean> {
    if (typeof navigator === "undefined" || !(navigator.storage && navigator.storage.persisted)) {
        return false;
    }
    return await navigator.storage.persisted();
}

/**
 * Ask for that exemption. A one-shot grant, and the browsers disagree on how it is decided: Chromium
 * answers from engagement heuristics without ever prompting, Firefox shows a permission prompt. Both
 * resolve false where unsupported, so a false is not necessarily a refusal.
 */
export async function persist(): Promise<boolean> {
    if (typeof navigator === "undefined" || !(navigator.storage && navigator.storage.persist)) {
        return false;
    }
    return await navigator.storage.persist();
}
