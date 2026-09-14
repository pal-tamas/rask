// The live protocol over plain HTTP, for a tab whose WebSocket never opened.
//
// Frames come DOWN a Server-Sent Events stream (GET /_rask/stream/{session}) and the tab's own frames go UP as
// POSTs (POST /_rask/send/{session}). It is the same protocol the socket carries — same hello semantics, same
// frames, same session — at the cost of one request per interaction. Chosen automatically: nothing in an app
// asks for it, and nothing turns it off.
//
// Deliberately DOM-free apart from `fetch`. The parser, the batcher and the chooser are plain functions over
// plain inputs, so a node fixture can drive each one with a fake clock, fake storage and fake responses — the
// only honest way to test a fallback whose whole point is the failure paths.

/** What the rest of the runtime needs from a live connection, whichever transport carries it. */
export interface LiveConnection {
    readonly isOpen: boolean;
    readonly isConnecting: boolean;
    send(message: string): void;
    close(code: number, reason: string): void;
}

// ---------------------------------------------------------------------------------------------------------------
// Server-Sent Events parsing
// ---------------------------------------------------------------------------------------------------------------

/**
 * Turns a stream of text chunks into events.
 *
 * Written rather than borrowed from EventSource, which reconnects on its own schedule (outside Rask's backoff and
 * overlay), cannot send a header (so the resume record would have to ride the URL, into proxy logs), and gives no
 * say over which stream generation a reconnect belongs to.
 *
 * The subset the server writes, handled to the letter of the format: `data:` lines join with a newline, `event:`
 * names the event, a line starting `:` is a comment (the server's heartbeat), and a blank line dispatches. A chunk
 * boundary can fall anywhere — mid-line, mid-`\r\n` — so the unfinished tail is carried to the next chunk.
 */
export function createSseParser(onEvent: (event: string, data: string) => void): (chunk: string) => void {
    let buffer = "";
    let event = "";
    let data: string[] = [];

    function line(text: string): void {
        if (text === "") {
            if (data.length > 0) onEvent(event || "message", data.join("\n"));
            event = "";
            data = [];
            return;
        }

        if (text.charCodeAt(0) === 58 /* ':' */) return;

        const colon = text.indexOf(":");
        const field = colon < 0 ? text : text.slice(0, colon);
        // One optional space after the colon belongs to the syntax, not to the value.
        let value = colon < 0 ? "" : text.slice(colon + 1);
        if (value.charCodeAt(0) === 32 /* ' ' */) value = value.slice(1);

        if (field === "data") data.push(value);
        else if (field === "event") event = value;
    }

    return function feed(chunk: string): void {
        buffer += chunk;
        let start = 0;
        for (;;) {
            const newline = buffer.indexOf("\n", start);
            if (newline < 0) break;
            let end = newline;
            if (end > start && buffer.charCodeAt(end - 1) === 13 /* '\r' */) end--;
            line(buffer.slice(start, end));
            start = newline + 1;
        }
        buffer = buffer.slice(start);
    };
}

// ---------------------------------------------------------------------------------------------------------------
// Sending: one POST in flight, everything else batched behind it
// ---------------------------------------------------------------------------------------------------------------

/**
 * Sends frames as POSTs, never more than one at a time.
 *
 * Order is the protocol's: a typing handler has to run before the submit that follows it, and the socket
 * guarantees that by being one stream. Two POSTs racing each other would not — so a frame sent while a POST is in
 * flight waits, and everything that piled up goes in the next one, as a single JSON array the server walks in order.
 *
 * `post` resolves with the response status. A refusal (anything but a 2xx) stops the batcher and reports the
 * status: the stream is what carries the recovery, and a batcher that kept posting into a refused session would
 * only produce more refusals.
 */
export function createPostBatcher(
    post: (body: string) => Promise<number>,
    onRefused: (status: number) => void,
): { push(message: string): void; readonly pending: number; stop(): void } {
    let queue: string[] = [];
    let inFlight = false;
    let stopped = false;

    function flush(): void {
        if (inFlight || stopped || queue.length === 0) return;
        const batch = queue;
        queue = [];
        inFlight = true;
        // Each message is already a serialised frame, so the array is assembled rather than re-serialised.
        post("[" + batch.join(",") + "]").then(
            (status) => {
                inFlight = false;
                if (status < 200 || status >= 300) {
                    stopped = true;
                    onRefused(status);
                    return;
                }
                flush();
            },
            () => {
                inFlight = false;
                stopped = true;
                onRefused(0);
            });
    }

    return {
        push(message: string): void {
            if (stopped) return;
            queue.push(message);
            flush();
        },
        get pending(): number {
            return queue.length + (inFlight ? 1 : 0);
        },
        stop(): void {
            stopped = true;
            queue = [];
        },
    };
}

// ---------------------------------------------------------------------------------------------------------------
// Choosing a transport
// ---------------------------------------------------------------------------------------------------------------

export type TransportKind = "ws" | "http";

/** The storage the choice is remembered in: sessionStorage in a page, a map in a fixture. */
export interface ChoiceStorage {
    getItem(key: string): string | null;
    setItem(key: string, value: string): void;
}

export const TRANSPORT_STORAGE_KEY = "rask:transport";

/** How long a WebSocket gets to open before the tab tries HTTP instead. */
export const OPEN_TIMEOUT_MS = 5000;

/** A socket that opens and dies within this long counts as cut off rather than as a blip. */
export const EARLY_CLOSE_MS = 5000;

/** How many early closes in a row mean the network is dropping sockets on purpose. */
export const EARLY_CLOSES_BEFORE_FALLBACK = 3;

/**
 * Decides which transport the next connection uses, and remembers what it learned for the tab.
 *
 * WebSocket first, always, unless this tab already proved it cannot have one. The proof is the part that matters:
 * a socket that fails to open is not enough, because a server that is simply down — mid-redeploy — fails exactly
 * the same way, and pinning the tab to HTTP for that would leave it on the slower transport for the rest of its
 * life for no reason. So a failed socket makes HTTP the *candidate*, and only an HTTP stream that then opens where
 * the socket did not is remembered. If HTTP fails too, it was an outage, and the next attempt is a socket again.
 *
 * Remembered per tab (sessionStorage), never for the site: the next tab tries the socket afresh, so a network
 * that stops blocking WebSockets is noticed.
 */
export function createTransportChooser(storage: ChoiceStorage | null, now: () => number) {
    let remembered: TransportKind = "ws";
    try {
        if (storage && storage.getItem(TRANSPORT_STORAGE_KEY) === "http") remembered = "http";
    } catch {
        // Storage can throw (a sandboxed frame, a full quota). The socket is the right default either way.
    }

    let candidate: TransportKind | null = null;
    let openedAt = 0;
    let earlyCloses = 0;

    function remember(kind: TransportKind): void {
        remembered = kind;
        try {
            if (storage) storage.setItem(TRANSPORT_STORAGE_KEY, kind);
        } catch {
            // Remembering is an optimisation; this tab still switched.
        }
    }

    return {
        /** The transport the next connection should use. */
        next(): TransportKind {
            return candidate ?? remembered;
        },

        /** Whether the tab has settled on HTTP for good. */
        get remembered(): TransportKind {
            return remembered;
        },

        wsOpened(): void {
            openedAt = now();
            candidate = null;
        },

        /** A socket that opened, then closed. `deliberate` for closes the runtime asked for itself. */
        wsClosed(deliberate: boolean): void {
            if (deliberate) return;
            if (now() - openedAt < EARLY_CLOSE_MS) {
                earlyCloses++;
                if (earlyCloses >= EARLY_CLOSES_BEFORE_FALLBACK) candidate = "http";
            } else {
                earlyCloses = 0;
            }
        },

        /** A socket that never opened: an error before `open`, or the open timeout. */
        wsFailedBeforeOpen(): void {
            candidate = "http";
        },

        httpOpened(): void {
            candidate = null;
            earlyCloses = 0;
            remember("http");
        },

        /** HTTP failed as well, so the socket's failure was the server, not the network. Back to the socket. */
        httpFailedBeforeOpen(): void {
            if (remembered !== "http") candidate = null;
        },
    };
}

// ---------------------------------------------------------------------------------------------------------------
// The connection itself
// ---------------------------------------------------------------------------------------------------------------

export interface HttpConnectionOptions {
    /** `/_rask/stream/{session}` with the app's base path applied. */
    streamUrl: string;
    /** `/_rask/send/{session}`. */
    sendUrl: string;
    /** `/_rask/leave/{session}`. */
    leaveUrl: string;
    /** The resume record, sent as a header so it never lands in a URL. */
    resumeToken: string | null;
    onOpen(): void;
    onFrame(text: string): void;
    /** `code` mirrors a WebSocket close: 1001 a server going away, 1008 a policy refusal, 1006 a dropped link. */
    onClose(code: number, reason: string, opened: boolean): void;
}

/**
 * Opens the stream and returns the connection. The connection is open once the stream has named its generation —
 * the frame that tells POSTs which stream they belong to — and not before.
 */
export function openHttpConnection(options: HttpConnectionOptions): LiveConnection & { leave(): void } {
    const abort = new AbortController();
    let generation: string | null = null;
    let open = false;
    let closed = false;

    function end(code: number, reason: string): void {
        if (closed) return;
        closed = true;
        const wasOpen = open;
        open = false;
        batcher.stop();
        try {
            abort.abort();
        } catch {
            // Already aborted.
        }
        options.onClose(code, reason, wasOpen);
    }

    const batcher = createPostBatcher(
        (body) => fetch(options.sendUrl, {
            method: "POST",
            headers: {"content-type": "application/json", "rask-stream": generation ?? ""},
            body,
            credentials: "same-origin",
        }).then((response) => response.status),
        // 409: a newer stream owns the session. 404: the session is gone. 429: a breaker tripped, and the
        // stream's own close event is on its way. All of them end this connection so the runtime reconnects.
        (status) => end(status === 429 ? 1008 : 1006, "send-refused-" + status));

    const parse = createSseParser((event, data) => {
        if (event === "close") {
            end(data === "server-shutdown" ? 1001 : data === "leave" || data === "session-unknown" ? 1000 : 1008, data);
            return;
        }

        if (!open) {
            try {
                const first = JSON.parse(data);
                if (first && first.type === "stream" && typeof first.generation === "number") {
                    generation = String(first.generation);
                    open = true;
                    options.onOpen();
                    return;
                }
            } catch {
                // Not the stream's opening frame; fall through and treat it as an ordinary one.
            }
        }

        options.onFrame(data);
    });

    const headers: Record<string, string> = {accept: "text/event-stream"};
    if (options.resumeToken) headers["rask-resume"] = options.resumeToken;

    fetch(options.streamUrl, {headers, credentials: "same-origin", signal: abort.signal, cache: "no-store"})
        .then(async (response) => {
            if (!response.ok || !response.body) {
                end(1006, "stream-" + response.status);
                return;
            }

            const reader = response.body.getReader();
            const decoder = new TextDecoder();
            for (;;) {
                const {done, value} = await reader.read();
                if (done) break;
                parse(decoder.decode(value, {stream: true}));
            }

            // The body ended without a close event: indistinguishable from a dropped link, so treat it as one.
            end(1006, "stream-ended");
        })
        .catch(() => end(1006, "stream-failed"));

    return {
        get isOpen(): boolean {
            return open;
        },
        get isConnecting(): boolean {
            return !open && !closed;
        },
        send(message: string): void {
            if (open) batcher.push(message);
        },
        close(code: number, reason: string): void {
            end(code, reason);
        },
        /**
         * Tells the server this tab is gone, so it frees the session now rather than after its grace period.
         * A keepalive fetch rather than sendBeacon, because the request has to say which stream it is — and a
         * beacon cannot carry a header.
         */
        leave(): void {
            if (!generation) return;
            try {
                void fetch(options.leaveUrl, {
                    method: "POST",
                    headers: {"rask-stream": generation},
                    credentials: "same-origin",
                    keepalive: true,
                });
            } catch {
                // The page is going away; there is nobody left to tell.
            }
        },
    };
}
