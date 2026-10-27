// Drives rask-resend.ts — what a connection carried that no ack answered, and what the next one sends first — and
// prints what it did as JSON for ResendFixtureTests.

import {createUnacked, MAX_UNACKED, newestSeq} from "../../../src/Rask.Server/Resources/rask-resend.js";

const results: Record<string, unknown> = {};

// Three clicks leave, the server answers the first: the other two go again, in the order they left.
{
    const u = createUnacked();
    u.sent("c1", 1);
    u.sent("c2", 2);
    u.sent("c3", 3);
    u.acked(1);
    results.afterAck = {again: u.again(), newest: u.newest};
}

// An ack for seq N answers every event up to N; a frame that carries no event is never held.
{
    const u = createUnacked();
    u.sent("nav", 0);
    u.sent("c1", 1);
    u.sent("c2", 2);
    u.acked(2);
    results.allAnswered = {again: u.again(), newest: u.newest};
}

// A sign-in lets go of everything, but the newest seq that left is still told to the next hello.
{
    const u = createUnacked();
    u.sent("c1", 1);
    u.forget();
    results.forgotten = {again: u.again(), newest: u.newest};
}

// Never more than MAX_UNACKED: the oldest goes.
{
    const u = createUnacked();
    for (let i = 1; i <= MAX_UNACKED + 2; i++) u.sent("c" + i, i);
    const again = u.again();
    results.capped = {count: again.length, first: again[0], max: MAX_UNACKED};
}

results.seqs = [
    newestSeq({id: "h", seq: 4}),
    newestSeq({type: "batch", events: [{id: "a", seq: 5}, {id: "b", seq: 7}]}),
    newestSeq({type: "navigate", path: "/"}),
    newestSeq(null),
];

console.log(JSON.stringify(results));
