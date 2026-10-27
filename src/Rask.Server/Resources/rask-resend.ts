// The events a connection carried that no ack answered, sent again on the next one.
//
// A click sent into a socket at the moment it dies is gone: the browser took it, the server never read it, and
// nothing said so. Every event carries a seq and every ack answers all of them up to its own, so what is still
// unanswered when a connection ends is known — and it leaves first on the next connection, in the order it was
// sent. Some of it DID arrive before the end; the session remembers the newest seq it took in and only answers
// those again (RaskEndpointExtensions.Resent), so each runs once.
//
// The newest seq that ever left goes in the next hello too: a page rebuilt from its resume record on another host
// cannot know which of them the lost one ran, and runs none.

/** The most frames held for the next connection; past it the oldest is let go. */
export const MAX_UNACKED = 64;

/** The newest event seq `frame` carries — itself, or a batch's — or 0 when it carries none. */
export function newestSeq(frame: unknown): number {
    const f = frame as { seq?: unknown; type?: unknown; events?: unknown } | null;
    if (!f || typeof f !== "object") return 0;
    if (f.type === "batch" && Array.isArray(f.events)) {
        let newest = 0;
        for (const e of f.events) newest = Math.max(newest, newestSeq(e));
        return newest;
    }
    return typeof f.seq === "number" ? f.seq : 0;
}

export interface Unacked {
    /** `message` left on the open connection; `seq` is the newest event it carries. */
    sent(message: string, seq: number): void;
    /** The server answered every event up to `seq`. */
    acked(seq: number): void;
    /** What a new connection sends before anything else, oldest first. */
    again(): readonly string[];
    /** Lets go of everything held: what it carried is not to be run (a sign-in drops it). */
    forget(): void;
    /** The newest seq that has left on any connection. */
    readonly newest: number;
}

export function createUnacked(): Unacked {
    let held: { message: string; seq: number }[] = [];
    let newest = 0;
    return {
        sent(message, seq) {
            if (seq <= 0) return;
            held.push({message, seq});
            if (held.length > MAX_UNACKED) held.shift();
            if (seq > newest) newest = seq;
        },
        acked(seq) {
            if (held.length > 0 && held[0].seq <= seq) held = held.filter((h) => h.seq > seq);
        },
        again() {
            return held.map((h) => h.message);
        },
        forget() {
            held = [];
        },
        get newest() {
            return newest;
        },
    };
}
