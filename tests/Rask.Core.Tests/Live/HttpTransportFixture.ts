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

results.batching = await batching();
results.refusal = await refusal();
results.choosing = choosing();

console.log(JSON.stringify(results));
