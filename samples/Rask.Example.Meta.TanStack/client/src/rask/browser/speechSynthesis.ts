// Speech synthesis — window.speechSynthesis.
//
// See ./geolocation.ts for the two rules every module in this directory follows.

export interface SpeakOptions {
    /** BCP 47 tag, e.g. "en-GB". Picks the voice; the browser falls back to the document language. */
    lang?: string | null;
    /** 0.1 – 10, default 1. */
    rate?: number | null;
    /** 0 – 2, default 1. */
    pitch?: number | null;
    /** 0 – 1, default 1. */
    volume?: number | null;
}

export function isSupported(): boolean {
    return typeof window !== "undefined" && "speechSynthesis" in window;
}

/**
 * Speak text aloud. A no-op where unsupported rather than a throw — speech is an enhancement, and a
 * caller that has to guard every call site would be worse off.
 *
 * Utterances queue: calling this twice speaks twice, one after the other. Use `cancel` to clear.
 */
export function speak(text: string, options?: SpeakOptions): void {
    if (!isSupported()) {
        return;
    }
    const u = new SpeechSynthesisUtterance(text);
    if (options) {
        if (options.lang) u.lang = options.lang;
        if (typeof options.rate === "number") u.rate = options.rate;
        if (typeof options.pitch === "number") u.pitch = options.pitch;
        if (typeof options.volume === "number") u.volume = options.volume;
    }
    window.speechSynthesis.speak(u);
}

/** Drop whatever is speaking and everything queued behind it. */
export function cancel(): void {
    if (isSupported()) {
        window.speechSynthesis.cancel();
    }
}
