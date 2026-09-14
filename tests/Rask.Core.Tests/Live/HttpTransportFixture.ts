// Drives the HTTP transport's pure parts — the event-stream parser, the POST batcher and the transport chooser —
// and prints what they did as JSON for HttpTransportFixtureTests to assert.
//
// No DOM, no network: each part takes plain inputs (text chunks, a fake post function, a fake clock and storage),
// which is what lets the failure paths of a fallback be tested at all.

import {
    createPostBatcher,
    createSseParser,
    createTransportChooser,
    EARLY_CLOSE_MS,
    framesThatFit,
    openHttpConnection,
    TRANSPORT_STORAGE_KEY,
} from "../../../src/Rask.Server/Resources/rask-http-transport.js";

type Result = Record<string, unknown>;
const results: Result = {};

// --- The parser --------------------------------------------------------------------------------------------------

function parse(chunks: string[]): Array<{ event: string; data: string }> {
    const events: Array<{ event: string; data: string }> = [];
    const feed = createSseParser((event, data) => events.push({event, data}));
    for (const chunk of chunks) feed(chunk);
    return events;
}

// A frame arriving whole.
results.whole = parse(['data: {"type":"stream","generation":7}\n\n']);

// The same frame split at every awkward place a network can split it: mid-field, mid-value, between \r and \n.
results.split = parse(['da', 'ta: {"type":', '"ack"}\r', '\n\r\n']);

// Comments are the server's heartbeat and carry nothing; a named event keeps its name; data lines join.
results.mixed = parse([':\n\n', 'event: close\ndata: server-shutdown\n\n', 'data: one\ndata: two\n\n']);

// A line with no colon is a field with an empty value, and a blank line with no data dispatches nothing.
results.degenerate = parse(['data\n\n', '\n\n']);

// --- The batcher -------------------------------------------------------------------------------------------------

async function batching(): Promise<Result> {
    const bodies: string[] = [];
    let release: (status: number) => void = () => {};

    const batcher = createPostBatcher(
        (body) => {
            bodies.push(body);
            return new Promise<number>((resolve) => { release = resolve; });
        },
        () => {});

    // The first goes out at once; the next three arrive while it is in flight and must wait.
    batcher.push('{"id":"a"}');
    batcher.push('{"id":"b"}');
    batcher.push('{"id":"c"}');
    batcher.push('{"id":"d"}');
    const inFlightBeforeRelease = bodies.length;

    release(204);
    await Promise.resolve();
    await Promise.resolve();

    release(204);
    await Promise.resolve();
    await Promise.resolve();

    return {inFlightBeforeRelease, bodies, pendingAfter: batcher.pending};
}

async function refusal(): Promise<Result> {
    const refusedWith: number[] = [];
    const bodies: string[] = [];

    const batcher = createPostBatcher(
        (body) => {
            bodies.push(body);
            return Promise.resolve(409);
        },
        (status) => refusedWith.push(status));

    batcher.push('{"id":"a"}');
    await Promise.resolve();
    await Promise.resolve();

    // After a refusal nothing more is sent: the stream carries the recovery.
    batcher.push('{"id":"b"}');
    await Promise.resolve();

    return {refusedWith, posts: bodies.length};
}

// --- The chooser -------------------------------------------------------------------------------------------------

function storage(initial: Record<string, string> = {}) {
    const map = new Map(Object.entries(initial));
    return {
        map,
        getItem: (key: string) => map.get(key) ?? null,
        setItem: (key: string, value: string) => { map.set(key, value); },
    };
}

function choosing(): Result {
    let clock = 0;
    const now = () => clock;

    // A fresh tab tries the socket.
    const fresh = createTransportChooser(storage(), now);
    const firstChoice = fresh.next();

    // The socket fails to open, but the server was merely down: HTTP fails too. That is an outage, not a network
    // that blocks sockets, so the tab goes back to the socket and remembers nothing.
    const outage = createTransportChooser(storage(), now);
    outage.wsFailedBeforeOpen();
    const duringOutage = outage.next();
    outage.httpFailedBeforeOpen();
    const afterOutage = outage.next();

    // The socket fails to open and HTTP opens: the network blocks sockets. Remembered for the tab.
    const blockedStore = storage();
    const blocked = createTransportChooser(blockedStore, now);
    blocked.wsFailedBeforeOpen();
    blocked.httpOpened();
    const rememberedValue = blockedStore.map.get(TRANSPORT_STORAGE_KEY) ?? null;

    // The same tab, reloaded, starts on HTTP without trying the socket again.
    const reloaded = createTransportChooser(blockedStore, now).next();

    // Sockets that open and die early, three times running, count as cut off.
    const cut = createTransportChooser(storage(), now);
    for (let i = 0; i < 3; i++) {
        clock += 100;
        cut.wsOpened();
        clock += EARLY_CLOSE_MS - 1;
        cut.wsClosed(false);
    }
    const afterThreeEarlyCloses = cut.next();

    // A socket that stays up a while and then blips is an ordinary reconnect, and resets the count.
    const blips = createTransportChooser(storage(), now);
    blips.wsOpened();
    clock += 100;
    blips.wsClosed(false);
    blips.wsOpened();
    clock += EARLY_CLOSE_MS + 1;
    blips.wsClosed(false);
    blips.wsOpened();
    clock += 100;
    blips.wsClosed(false);
    const afterBlips = blips.next();

    // A close the runtime asked for — a sign-in, an expired session — is never evidence against the socket.
    const deliberate = createTransportChooser(storage(), now);
    for (let i = 0; i < 5; i++) {
        deliberate.wsOpened();
        clock += 10;
        deliberate.wsClosed(true);
    }
    const afterDeliberateCloses = deliberate.next();

    return {
        firstChoice,
        duringOutage,
        afterOutage,
        rememberedValue,
        reloaded,
        afterThreeEarlyCloses,
        afterBlips,
        afterDeliberateCloses,
    };
}

// --- A whole connection, over a stubbed fetch ---------------------------------------------------------------------

const tick = () => new Promise<void>((resolve) => setTimeout(resolve, 5));

async function connection(): Promise<Result> {
    const encoder = new TextEncoder();
    let push: (text: string) => void = () => {};
    const requests: Array<{ url: string; method: string; stream: string | null; body: string | null }> = [];

    const realFetch = globalThis.fetch;
    globalThis.fetch = (async (input: string, init: RequestInit = {}) => {
        const headers = new Headers(init.headers);
        requests.push({
            url: input,
            method: init.method ?? "GET",
            stream: headers.get("rask-stream"),
            body: typeof init.body === "string" ? init.body : null,
        });

        if ((init.method ?? "GET") === "GET") {
            const body = new ReadableStream<Uint8Array>({
                start(controller) {
                    push = (text) => controller.enqueue(encoder.encode(text));
                },
            });
            return new Response(body, {status: 200, headers: {"content-type": "text/event-stream"}});
        }

        return new Response(null, {status: 204});
    }) as typeof fetch;

    const events: string[] = [];
    try {
        const conn = openHttpConnection({
            sessionId: "old-id",
            url: (kind, session) => "/_rask/" + kind + "/" + session,
            resumeToken: "record",
            onOpen: (session) => events.push("open:" + session),
            onFrame: (text) => events.push("frame:" + text),
            onClose: (code, reason, opened) => events.push("close:" + code + ":" + reason + ":" + opened),
        });
        await tick();

        // A rebuild: the attach renders the new session before the stream names it.
        push('data: {"html":"rebuilt"}\n\n');
        await tick();
        const beforeOpen = events.slice();
        const sendWhileConnecting = conn.isOpen;

        push('data: {"type":"stream","session":"new-id","generation":3}\n\n');
        await tick();

        conn.send('{"id":"h1"}');
        await tick();
        conn.leave();
        await tick();

        push("event: close\ndata: server-shutdown\n\n");
        await tick();

        return {
            beforeOpen,
            sendWhileConnecting,
            events,
            streamUrl: requests[0]?.url ?? null,
            posts: requests.slice(1).map((r) => ({url: r.url, stream: r.stream, body: r.body})),
        };
    } finally {
        globalThis.fetch = realFetch;
    }
}

// Frames packed to the announced limit: 12 bytes of brackets and one frame, then 11 per further frame.
results.packing = {
    unlimited: framesThatFit(['{"id":"a"}', '{"id":"b"}', '{"id":"c"}'], 0),
    twoFit: framesThatFit(['{"id":"a"}', '{"id":"b"}', '{"id":"c"}'], 23),
    oversizedStillGoes: framesThatFit(['{"id":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}', '{"id":"b"}'], 10),
    multibyte: framesThatFit(['"é"', '"é"'], 9),
};

// Deadlines: a stream that never opens, and one that opens and then goes silent.
async function deadlines(): Promise<Result> {
    const realFetch = globalThis.fetch;
    let push: (text: string) => void = () => {};
    globalThis.fetch = (async () => {
        const body = new ReadableStream<Uint8Array>({
            start(controller) {
                push = (text) => controller.enqueue(new TextEncoder().encode(text));
            },
        });
        return new Response(body, {status: 200});
    }) as typeof fetch;

    const events: string[] = [];
    const options = {
        sessionId: "s",
        url: (kind: string, session: string) => "/_rask/" + kind + "/" + session,
        resumeToken: null,
        onOpen: () => events.push("open"),
        onFrame: () => {},
        onClose: (code: number, reason: string, opened: boolean) => events.push(code + ":" + reason + ":" + opened),
    };

    try {
        openHttpConnection({...options, openTimeoutMs: 20, silenceMs: 10_000});
        await new Promise((resolve) => setTimeout(resolve, 60));
        const neverOpened = events.splice(0);

        openHttpConnection({...options, openTimeoutMs: 10_000, silenceMs: 30});
        await tick();
        push('data: {"type":"stream","generation":1}\n\n');
        await new Promise((resolve) => setTimeout(resolve, 80));
        const wentSilent = events.splice(0);

        return {neverOpened, wentSilent};
    } finally {
        globalThis.fetch = realFetch;
    }
}

results.connection = await connection();
results.deadlines = await deadlines();
results.batching = await batching();
results.refusal = await refusal();
results.choosing = choosing();

console.log(JSON.stringify(results));
