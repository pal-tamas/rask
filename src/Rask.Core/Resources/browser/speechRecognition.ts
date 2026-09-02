// Dictation — SpeechRecognition / webkitSpeechRecognition.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
//
// Chromium-family only, and the first start prompts for the microphone. On Chrome the audio goes to
// Google's servers for recognition — worth knowing before you put it in front of a user, and worth
// saying out loud rather than leaving them to find out.

export interface SpeechResult {
    transcript: string;
    /** False for an interim guess the engine may revise, true once it has settled. */
    isFinal: boolean;
    /** 0–1, or 0 when the engine does not report one. */
    confidence: number;
}

export interface SpeechOptions {
    /** BCP 47 tag. Defaults to the document language. */
    lang?: string | null;
    /** Keep listening through pauses. Without this the engine stops on the first silence. */
    continuous?: boolean;
    /** Report interim guesses as well as settled results. */
    interimResults?: boolean;
}

function constructor(): (new() => SpeechRecognitionLike) | undefined {
    return window.SpeechRecognition || window.webkitSpeechRecognition;
}

export function isSupported(): boolean {
    return typeof window !== "undefined" && !!constructor();
}

/**
 * Start listening. Returns the stop function, which also releases the microphone.
 *
 * `continuous` is implemented by restarting: the engine ends the session on silence regardless of the
 * flag, so the `end` event restarts it until you stop. Two things make that safe — a terminal
 * permission error latches the session stopped so the restart cannot become a loop, and a restart that
 * races an already-starting engine throws, which is caught and ignored.
 */
export function start(
    onResult: (result: SpeechResult) => void,
    options?: SpeechOptions): () => void {
    const C = constructor();
    if (!C) {
        return () => { /* nothing was started */ };
    }

    const continuous = !!(options && options.continuous);
    let stopped = false;

    const rec = new C();
    if (options && options.lang) {
        rec.lang = options.lang;
    }
    rec.continuous = continuous;
    rec.interimResults = !!(options && options.interimResults);

    rec.onresult = (e: SpeechRecognitionEventLike) => {
        for (let i = e.resultIndex; i < e.results.length; i++) {
            const r = e.results[i];
            const alt = r[0];
            onResult({
                transcript: alt ? alt.transcript : "",
                isFinal: !!r.isFinal,
                confidence: alt && isFinite(alt.confidence) ? alt.confidence : 0
            });
        }
    };

    rec.onerror = (e: { error?: string }) => {
        // A permission or service error is terminal. Without this latch, `onend` restarts straight
        // into the same error, forever.
        if (e && (e.error === "not-allowed" || e.error === "service-not-allowed")) {
            stopped = true;
        }
    };

    rec.onend = () => {
        if (continuous && !stopped) {
            try {
                rec.start();
            } catch {
                // Already (re)starting — ignore.
            }
        }
    };

    rec.start();

    return () => {
        if (stopped) {
            return;
        }
        stopped = true;
        try {
            rec.stop();
        } catch (e) {
            void e; // not started — ignore
        }
    };
}
