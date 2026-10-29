// Drives rask-auth-watch.ts — when a tab asks whether the browser's credential still names its user, and what
// happens to the frames the page sends meanwhile — and prints what it did as JSON for AuthWatchFixtureTests.
//
// The host here is the smallest one that sends the way the Server runtime does: a frame goes out on the open
// connection unless the watch holds it, and what was held leaves when the watch lets go or a connection opens.

import {
    createAuthWatch,
    FRESH_FOCUSED_MS,
    FRESH_UNFOCUSED_MS,
    HOLD_MS,
    RETRIES,
    RETRY_MS,
    type WhoAnswer,
} from "../../../src/Rask.Server/Resources/rask-auth-watch.js";

interface Tab {
    send(frame: string): void;
    answer(answer: WhoAnswer | null | "fails"): Promise<void>;
    /** Lets `ms` of the page's time pass for whatever waits that long. */
    elapse(ms: number): void;
    reconnected(): void;
    /** The connection drops by itself, as a network does. */
    drop(): void;
    watch: ReturnType<typeof createAuthWatch>;
    state: { now: number; focused: boolean; visible: boolean; http: boolean; dropFails: boolean };
    /** Each frame that left, with the connection it left on: "1:click". */
    wire: string[];
    asks: number;
    reopens: number;
    told: number;
    /** The browser's other tabs: what this one tells, they hear. */
    others: Tab[];
    /** What is waiting for time to pass. */
    waiting: Array<{ again: () => void; ms: number }>;
}

function tab(bootedWithUser = true): Tab {
    const state = {now: 1_000_000, focused: true, visible: true, http: false, dropFails: false};
    const wire: string[] = [];
    const queue: string[] = [];
    const owed: Array<{ resolve: (a: WhoAnswer | null) => void; reject: (e: Error) => void }> = [];
    let connection = 1;
    let open = true;

    function flush(): void {
        if (!open) return;
        for (const frame of queue) wire.push(connection + ":" + frame);
        queue.length = 0;
    }

    const me: Tab = {
        state, wire, asks: 0, reopens: 0, told: 0, others: [], waiting: [],
        watch: createAuthWatch({
            ask() {
                me.asks++;
                return new Promise<WhoAnswer | null>((resolve, reject) => owed.push({resolve, reject}));
            },
            reopen() {
                me.reopens++;
                // As the runtime's: nothing to drop without an open connection, and a close that throws drops nothing.
                if (!open || state.dropFails) return false;
                open = false;
                return true;
            },
            release: flush,
            tellOthers() {
                me.told++;
                for (const other of me.others) other.watch.noticed(true);
            },
            wait(again, ms) {
                me.waiting.push({again, ms});
            },
            now: () => state.now,
            focused: () => state.focused,
            visible: () => state.visible,
            cookieOnEveryFrame: () => state.http,
            bootedWithUser,
        }),
        send(frame: string) {
            if (open && !me.watch.holds()) wire.push(connection + ":" + frame);
            else queue.push(frame);
        },
        async answer(answer) {
            const asked = owed.shift()!;
            if (answer === "fails") asked.reject(new Error("offline"));
            else asked.resolve(answer);
            // The watch hears the answer in a microtask of its own.
            await new Promise<void>((done) => setImmediate(done));
        },
        elapse(ms) {
            const due = me.waiting.filter((w) => w.ms <= ms);
            me.waiting = me.waiting.filter((w) => w.ms > ms);
            for (const w of due) w.again();
        },
        drop() {
            open = false;
        },
        reconnected() {
            connection++;
            open = true;
            me.watch.opened();
            flush();
        },
    };
    return me;
}

const results: Record<string, unknown> = {};

// Straight after the page loaded, the request that loaded it is the answer: nothing is asked, nothing waits.
{
    const t = tab();
    t.send("click");
    results.fresh = {asks: t.asks, wire: t.wire};
}

// Left and come back to: it asks at once, and what the reader does before the answer waits for it.
{
    const t = tab();
    t.watch.left();
    t.watch.noticed();
    const askedOnReturn = t.asks;
    t.send("click");
    const wireWhileAsking = t.wire.slice();
    await t.answer({same: true, user: true});
    results.cameBackSame = {askedOnReturn, wireWhileAsking, wire: t.wire, asks: t.asks, reopens: t.reopens};
}

// The credential changed while it was away: the connection is opened again BEFORE anything is sent, and what the
// reader did leaves on the new one, in order.
{
    const t = tab();
    t.watch.left();
    t.watch.noticed();
    t.send("type");
    await t.answer({same: false});
    t.send("click");
    const wireBeforeReconnect = t.wire.slice();
    t.reconnected();
    t.send("after");
    results.cameBackChanged = {
        wireBeforeReconnect, wire: t.wire, reopens: t.reopens, told: t.told, asks: t.asks,
    };
}

// Hidden, and something sends anyway (a timer's event, a script's): it asks first, with nobody looking.
{
    const t = tab();
    t.state.visible = false;
    t.watch.left();
    t.watch.noticed();
    const asksWhileHidden = t.asks;
    t.send("tick");
    const asksOnFrame = t.asks;
    await t.answer({same: true, user: true});
    results.hidden = {asksWhileHidden, asksOnFrame, wire: t.wire};
}

// Nobody left and nobody came back, and the cookie could still have been swapped under a focused window: an
// answer is trusted for so long and no longer.
{
    const t = tab();
    t.state.now += FRESH_FOCUSED_MS;
    t.send("within");
    const asksWithin = t.asks;
    t.state.now += 1;
    t.send("beyond");
    const asksBeyond = t.asks;
    await t.answer({same: true, user: true});
    t.send("fresh again");
    results.agedFocused = {asksWithin, asksBeyond, asks: t.asks, wire: t.wire};
}

// A window without the focus is reached by a pointer only, and trusted for much less.
{
    const t = tab();
    t.state.focused = false;
    t.state.now += FRESH_UNFOCUSED_MS;
    t.send("within");
    const asksWithin = t.asks;
    t.state.now += 1;
    t.send("beyond");
    results.agedUnfocused = {asksWithin, asksBeyond: t.asks, wire: t.wire};
}

// No answer is not "changed": a question that fails, times out or is answered with nothing keeps the connection,
// lets what waited go on it, and the next trigger asks again.
{
    const failed = tab();
    failed.watch.left();
    failed.watch.noticed();
    failed.send("click");
    await failed.answer("fails");
    failed.send("after");
    const empty = tab();
    empty.watch.left();
    empty.watch.noticed();
    empty.send("click");
    await empty.answer(null);
    empty.watch.noticed();
    results.unanswered = {
        reopens: failed.reopens + empty.reopens, told: failed.told + empty.told,
        wire: failed.wire, emptyWire: empty.wire, asksAfterNextTrigger: empty.asks,
    };
}

// An answer slow to come holds what the page sends for a moment of the page's time and no longer: it leaves on the
// connection that is open, and so does what is sent while the answer is still out. The answer is still acted on.
{
    const t = tab();
    t.watch.left();
    t.watch.noticed();
    t.send("click");
    const wireWithin = t.wire.slice();
    t.elapse(HOLD_MS);
    t.send("after");
    const wireLate = t.wire.slice();
    await t.answer({same: true, user: true});
    const slowSame = {wireWithin, wireLate, reopens: t.reopens};
    const changed = tab();
    changed.watch.left();
    changed.watch.noticed();
    changed.send("click");
    changed.elapse(HOLD_MS);
    await changed.answer({same: false});
    changed.send("held");
    changed.reconnected();
    results.slow = {...slowSame, changedWire: changed.wire, changedReopens: changed.reopens};
}

// A hold that lapses after its answer came lets nothing go twice and asks nothing.
{
    const t = tab();
    t.watch.left();
    t.watch.noticed();
    t.send("click");
    await t.answer({same: true, user: true});
    t.elapse(HOLD_MS);
    results.lapsedAfterAnswer = {wire: t.wire, asks: t.asks};
}

// "Later" every time is no answer either: the connection stays.
{
    const t = tab();
    t.watch.left();
    t.watch.noticed();
    for (let i = 0; i <= RETRIES; i++) {
        await t.answer({later: true});
        t.elapse(RETRY_MS);
    }
    results.laterForEver = {reopens: t.reopens, asks: t.asks};
}

// Told three times while one question is out: one question.
{
    const t = tab();
    t.watch.noticed();
    t.watch.noticed();
    t.send("click");
    t.watch.noticed();
    results.oneAtATime = {asks: t.asks};
}

// Over HTTP every frame is a request with the cookie in it: nothing to ask, nothing to hold.
{
    const t = tab();
    t.state.http = true;
    t.watch.left();
    t.watch.noticed();
    t.state.now += FRESH_FOCUSED_MS * 10;
    t.send("click");
    results.http = {asks: t.asks, wire: t.wire};
}

// What the tab knows of itself: somebody as it booted, or not, and whatever the last answer said since.
{
    const signedIn = tab(true);
    const anonymous = tab(false);
    const bootedAnonymous = anonymous.watch.hadUser;
    anonymous.watch.noticed();
    await anonymous.answer({same: true, user: true});
    results.hadUser = {booted: signedIn.watch.hadUser, bootedAnonymous, afterAnswer: anonymous.watch.hadUser};
}

// Three tabs side by side. One finds the credential changed and tells the others; they ask, find the same, and
// open their connections again — and say nothing, or the three would wake each other for ever.
{
    const tabs = [tab(), tab(), tab()];
    for (const t of tabs) t.others = tabs.filter((other) => other !== t);
    tabs[0].watch.left();
    tabs[0].watch.noticed();
    await tabs[0].answer({same: false});
    await tabs[1].answer({same: false});
    await tabs[2].answer({same: false});
    for (const t of tabs) t.reconnected();
    results.threeTabs = {
        told: tabs.map((t) => t.told),
        asks: tabs.map((t) => t.asks),
        reopens: tabs.map((t) => t.reopens),
    };
}

// Asked too often, the server says "later": that is not "changed". The frame goes on waiting, the question is put
// again, and nothing is opened again.
{
    const t = tab();
    t.watch.left();
    t.watch.noticed();
    t.send("click");
    await t.answer({later: true});
    const afterLater = {reopens: t.reopens, wire: t.wire.slice(), waiting: t.waiting.filter((w) => w.ms === RETRY_MS).length};
    t.elapse(RETRY_MS);
    await t.answer({same: true, user: true});
    results.later = {afterLater, asks: t.asks, reopens: t.reopens, wire: t.wire};
}

// "Changed", and the connection cannot be dropped — it throws on close: nothing will open to end the hold, so what
// waited leaves on the connection that is still open, and so does what is sent after. Before, it waited for ever.
{
    const t = tab();
    t.state.dropFails = true;
    t.watch.left();
    t.watch.noticed();
    t.send("click");
    await t.answer({same: false});
    t.send("after");
    results.dropFailed = {wire: t.wire, reopens: t.reopens, holds: t.watch.holds()};
}

// "Changed" while the connection is already down: nothing is dropped, and the hold does not outlive it — the next
// connection opens with the cookie as it is then, and everything leaves on it, in order.
{
    const t = tab();
    t.watch.left();
    t.watch.noticed();
    t.send("click");
    t.drop();
    await t.answer({same: false});
    const holdsWhileDown = t.watch.holds();
    t.send("down");
    t.reconnected();
    t.send("after");
    results.changedWhileDown = {holdsWhileDown, wire: t.wire};
}

console.log(JSON.stringify(results));
