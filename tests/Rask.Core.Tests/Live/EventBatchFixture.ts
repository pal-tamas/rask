// Node-driven fixture for rask-batch.ts — the events one task produces leave as one frame.
//
// The host here is the smallest one that batches the way both runtimes do: a handler event is held until the
// task ends, anything else is sent at once behind what was held. The C# test (EventBatchTests) runs this in a
// node subprocess and asserts the JSON line.

import {listeners, StubField} from "./stub-events.js";
import {setHost} from "../../../src/Rask.Core/Resources/rask-host.js";
import {flushInputsNow} from "../../../src/Rask.Core/Resources/rask-input.js";
import {eventBatch, isHandlerEvent, MAX_BATCH} from "../../../src/Rask.Core/Resources/rask-batch.js";

let frames: unknown[] = [];
let owed: (() => void)[] = [];

function ship(frame: unknown): Promise<void> {
    frames.push(frame);
    return new Promise<void>(function (answer) { owed.push(answer); });
}

const events = eventBatch(ship);

function send(payload: unknown): unknown {
    if (isHandlerEvent(payload)) {
        return events.add(payload);
    }
    events.flush();
    frames.push(payload);
    return undefined;
}

setHost({send, inRoot: () => true});

// The end of the task: every microtask queued so far has run.
async function taskEnds(): Promise<void> {
    await new Promise<void>(function (done) { setTimeout(done, 0); });
}

async function answer(): Promise<void> {
    owed.shift()!();
    await taskEnds();
}

function start(): void {
    frames = [];
    owed = [];
}

function click(n: number): { id: string; type: string } {
    return {id: "h" + n, type: "click"};
}

// Sixty events in one task: nothing leaves while the task runs, and one frame leaves when it ends.
start();
for (let i = 0; i < 60; i++) send(click(i));
const duringTask = frames.length;
await taskEnds();
const sixty = frames.slice();

// One event travels as the frame it always was.
start();
send(click(1));
await taskEnds();
const alone = frames.slice();

// Events of two tasks are two frames.
start();
send(click(1));
await taskEnds();
send(click(2));
await taskEnds();
const twoTasks = frames.slice();

// A navigation does not overtake the events that happened before it, and what follows it is a batch of its own.
start();
send(click(1));
send(click(2));
send({type: "navigate", path: "/next"});
send(click(3));
const atNavigation = frames.slice();
await taskEnds();
const afterNavigation = frames.slice();

// More than a frame may carry go in a frame of their own.
start();
for (let i = 0; i < MAX_BATCH + 1; i++) send(click(i));
await taskEnds();
const overflow = frames.map(function (frame) {
    const batch = frame as { events?: unknown[] };
    return batch.events ? batch.events.length : 1;
});

// Every event of a batch is answered when the frame it left in is, and not before.
start();
let answered = 0;
for (let i = 0; i < 3; i++) (send(click(i)) as Promise<void>).then(function () { answered++; });
await taskEnds();
const answeredBefore = answered;
await answer();
const answeredAfter = answered;

// What is not an event for a handler is never held.
const kinds = [
    isHandlerEvent(click(1)),
    isHandlerEvent({id: 7, type: "jsResult"}),
    isHandlerEvent({type: "navigate", path: "/"}),
    isHandlerEvent(null),
];

// Typed text flushed ahead of a click leaves in the same frame, ahead of it.
start();
let field = new StubField({"data-rask-on-input": "h1"});
field.value = "Atlantis";
listeners.input({target: field});
flushInputsNow();
send(click(2));
await taskEnds();
const typedThenClicked = frames.slice();

// Text typed while the first letter's frame is unanswered is still held, and goes when that frame is answered.
start();
field = new StubField({"data-rask-on-input": "h1", "data-rask-on-change": "h2"});
field.value = "A";
listeners.input({target: field});
field.value = "Atlantis";
listeners.input({target: field});
await taskEnds();
const heldWhileOwed = frames.slice();
await answer();
const sentOnceAnswered = frames.slice();

process.stdout.write(JSON.stringify({
    duringTask,
    sixty,
    alone,
    twoTasks,
    atNavigation,
    afterNavigation,
    overflow,
    answeredBefore,
    answeredAfter,
    kinds,
    typedThenClicked,
    heldWhileOwed,
    sentOnceAnswered,
}));
