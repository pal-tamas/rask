// Node-driven fixture for rask-input.ts — what is typed into a field while .NET still owes it an answer.
//
// The host here answers when the fixture says so, which is the slow path a browser only reaches under load:
// a page whose render takes longer than a key. The C# test (InputHoldTests) runs this in a node subprocess
// and asserts the JSON line.

import {frame, listeners, StubField} from "./stub-events.js";
import {setHost} from "../../../src/Rask.Core/Resources/rask-host.js";
import {flushInputsNow} from "../../../src/Rask.Core/Resources/rask-input.js";

let sent: string[] = [];
let owed: (() => void)[] = [];
let answers = true;

setHost({
    send(payload: unknown) {
        const event = payload as { type: string; value: string };
        sent.push(event.value);
        return answers ? new Promise<void>(function (answer) { owed.push(answer); }) : undefined;
    },
    inRoot: () => true,
});

function type(field: StubField, value: string): void {
    field.value = value;
    listeners.input({target: field});
}

// The handler ran and its render is on the page: the oldest value sent is answered.
async function answer(): Promise<void> {
    owed.shift()!();
    await Promise.resolve();
    await Promise.resolve();
}

function start(): StubField {
    sent = [];
    owed = [];
    return new StubField({"data-rask-on-input": "h1", "data-rask-on-change": "h2"});
}

// Eight letters, typed before the first is answered: the first goes, and then only what the field says.
let field = start();
"Atlantis".split("").forEach(function (_, i) { type(field, "Atlantis".slice(0, i + 1)); });
const whileOwed = sent.slice();
await answer();
const afterAnswer = sent.slice();
await answer();
type(field, "Atlantis!");
const afterAllAnswered = sent.slice();

// A key, a click, the change: whatever comes next must find the page knowing what was typed.
field = start();
type(field, "A");
type(field, "At");
flushInputsNow();
const flushed = sent.slice();
type(field, "Atl");
await answer();
const oneOfTwoAnswered = sent.slice();
await answer();
const bothAnswered = sent.slice();

// A field that is gone when its answer comes sends nothing more.
field = start();
type(field, "A");
type(field, "At");
field.isConnected = false;
await answer();
const gone = sent.slice();

// The answer to "A" is written into a field already left, over what was typed since. What was typed is sent.
field = start();
type(field, "A");
type(field, "Atlantis");
field.value = "A";
await answer();
const overwritten = sent.slice();

// A field with no change handler is sent once a frame, and held the same way.
field = start();
const framed = new StubField({"data-rask-on-input": "h3"});
type(framed, "T");
type(framed, "Te");
const beforeFrame = sent.slice();
frame();
const firstFrame = sent.slice();
type(framed, "Tex");
frame();
const heldAcrossFrame = sent.slice();
await answer();
const frameAnswered = sent.slice();

// A host that does not say when it has answered — the Server's socket — is sent every value.
field = start();
answers = false;
type(field, "A");
type(field, "At");
type(field, "Atl");
const unansweredHost = sent.slice();

process.stdout.write(JSON.stringify({
    whileOwed,
    afterAnswer,
    afterAllAnswered,
    flushed,
    oneOfTwoAnswered,
    bothAnswered,
    gone,
    overwritten,
    beforeFrame,
    firstFrame,
    heldAcrossFrame,
    frameAnswered,
    unansweredHost,
}) + "\n");
