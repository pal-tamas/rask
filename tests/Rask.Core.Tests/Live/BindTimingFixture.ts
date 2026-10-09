// Node-driven fixture for rask-input.ts — a bound field that waits: `.Debounce(…)` (data-rask-debounce beside
// data-rask-on-input) and `.Blur()` (data-rask-bind-on beside data-rask-on-change).
//
// Time passes when the fixture says so (stub-events.ts). The C# test (BindTimingClientTests) runs this in a
// node subprocess and asserts the JSON line.

import {elapse, listeners, StubField} from "./stub-events.js";
import {setHost} from "../../../src/Rask.Core/Resources/rask-host.js";
import {changeSent, flushInputsNow} from "../../../src/Rask.Core/Resources/rask-input.js";

let sent: string[] = [];
let owed: (() => void)[] = [];
let answers = false;

setHost({
    send(payload: unknown) {
        const event = payload as { id: string; type: string; value?: string };
        sent.push(event.value === undefined ? event.type + ":" + event.id : event.type + ":" + event.value);
        return answers ? new Promise<void>(function (answer) { owed.push(answer); }) : undefined;
    },
    inRoot: () => true,
});

function type(field: StubField, value: string, isComposing = false): void {
    field.value = value;
    listeners.input({target: field, isComposing});
}

async function answer(): Promise<void> {
    owed.shift()!();
    await Promise.resolve();
    await Promise.resolve();
}

// Each case starts with nothing waiting from the one before it.
function settle(): void {
    flushInputsNow();
    elapse(60_000);
}

function debounced(extra: Record<string, string> = {}): StubField {
    settle();
    sent = [];
    owed = [];
    return new StubField({"data-rask-on-input": "h1", "data-rask-debounce": "300", ...extra});
}

function blurBound(): StubField {
    settle();
    sent = [];
    return new StubField({"data-rask-on-change": "h2", "data-rask-bind-on": "blur"});
}

// Three letters, then a pause: nothing while typing, one value at the pause, nothing after it.
let field = debounced();
type(field, "A");
elapse(200);
type(field, "At");
elapse(200);
type(field, "Atl");
elapse(299);
const beforePause = sent.slice();
elapse(1);
const atPause = sent.slice();
elapse(1000);
const afterPause = sent.slice();

// Enter's submit, a click, a handled key: what the pause still holds goes first, and the timer says no more.
field = debounced();
type(field, "At");
flushInputsNow();
const flushed = sent.slice();
elapse(1000);
flushInputsNow();
const flushedOnce = sent.slice();

// Leaving the field: the browser's `change` sends what the pause was holding.
field = debounced();
type(field, "Atl");
listeners.change({target: field});
elapse(1000);
const left = sent.slice();

// A character being composed counts no pause; the end of the composition starts one.
field = debounced();
type(field, "あ", true);
elapse(1000);
const composing = sent.slice();
listeners.compositionend({target: field});
elapse(299);
const composedTooSoon = sent.slice();
elapse(1);
const composed = sent.slice();

// A host that says when it has answered (WASM): a pause that ends while .NET still owes the answer to the
// last one is held, and goes as the field's latest value when the answer comes.
answers = true;
field = debounced();
type(field, "A");
elapse(300);
type(field, "At");
elapse(300);
const heldAtPause = sent.slice();
await answer();
const heldSent = sent.slice();
answers = false;

// Emptied by a key, with no `input` (data-rask-clear-keys): what goes is what the field says.
field = debounced();
type(field, "Atl");
field.value = "";
flushInputsNow();
elapse(1000);
const cleared = sent.slice();

// A message under the field: the first keystroke says so, once, and the value still waits for its pause.
field = debounced({"data-rask-on-edit": "h9"});
type(field, "A");
type(field, "At");
const edited = sent.slice();
const editStillAsked = field.hasAttribute("data-rask-on-edit");

// Bound on blur: typing sends nothing. Whatever must follow the value sends it as the change it is — and the
// `change` the browser then fires for that same value is not sent a second time.
field = blurBound();
type(field, "A");
type(field, "At");
elapse(5000);
const typedIntoBlur = sent.slice();
flushInputsNow();
flushInputsNow();
const committedOnce = sent.slice();
const duplicate = changeSent(field as unknown as Element);
const askedAgain = changeSent(field as unknown as Element);

// Typed into again after it was sent: the browser's change is news, and the host is left to send it.
field = blurBound();
type(field, "A");
flushInputsNow();
type(field, "At");
const changedSince = changeSent(field as unknown as Element);

// The ordinary way out: the browser fires `change`, the host sends it, and nothing is left to flush.
field = blurBound();
type(field, "A");
listeners.change({target: field});
const ownedByHost = changeSent(field as unknown as Element);
flushInputsNow();
const afterNativeChange = sent.slice();

process.stdout.write(JSON.stringify({
    beforePause,
    atPause,
    afterPause,
    flushed,
    flushedOnce,
    left,
    composing,
    composedTooSoon,
    composed,
    heldAtPause,
    heldSent,
    cleared,
    edited,
    editStillAsked,
    typedIntoBlur,
    committedOnce,
    duplicate,
    askedAgain,
    changedSince,
    ownedByHost,
    afterNativeChange,
}) + "\n");
