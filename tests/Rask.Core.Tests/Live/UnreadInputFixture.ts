// Node-driven fixture for rask-input.ts — a number, date or time input holding text the browser will not hand
// over (`validity.badInput`, value ""), and a submit the browser refuses over one.
//
// The host here asks rask-input to send what waits first, as both real hosts do (sendTypedFirst). A frame is
// written `type:value`, with `!kind` after it when it says the field held text back. Time passes when the
// fixture says so (stub-events.ts). The C# test (UnreadInputClientTests) asserts the JSON line.

import {elapse, listeners, StubField} from "./stub-events.js";
import {setHost} from "../../../src/Rask.Core/Resources/rask-host.js";
import {sendTypedFirst} from "../../../src/Rask.Core/Resources/rask-input.js";

let sent: string[] = [];

function hostSend(payload: unknown): unknown {
    sendTypedFirst(payload);
    const event = payload as { id?: string; type: string; value?: string; bad?: string };
    const said = event.value === undefined ? event.type + ":" + (event.id ?? "") : event.type + ":" + event.value;
    sent.push(event.bad ? said + "!" + event.bad : said);
    return undefined;
}

setHost({send: hostSend, inRoot: () => true});

/** A form as the browser hands one to a listener: its fields, and the submits dispatched on it. */
class StubForm {
    elements: StubField[] = [];
    submits = 0;

    /** Whether the page's rules decide if the form's handler runs: false for one that takes what was posted. */
    constructor(private readonly validated = true) {
    }

    hasAttribute(name: string): boolean {
        return name === "data-rask-on-submit" || (name === "data-rask-unvalidated" && !this.validated);
    }

    // What the host's own `submit` listener does with it.
    dispatchEvent(): boolean {
        this.submits++;
        hostSend({id: "form", type: "submit"});
        return true;
    }

    add(field: StubField): StubField {
        field.form = this;
        this.elements.push(field);
        return field;
    }
}

const page = globalThis as unknown as Record<string, unknown>;
page.SubmitEvent = class {
};

function field(type: string, extra: Record<string, string>): StubField {
    return new StubField({type, ...extra});
}

const waits = {"data-rask-on-change": "weight", "data-rask-bind-on": "action"};

// What the browser makes of a lone minus in a number field: nothing to read, and it says so.
function typeUnreadable(input: StubField): void {
    input.value = "";
    input.validity = {valid: false, badInput: true};
    listeners.input({target: input});
}

function type(input: StubField, value: string, validity: Record<string, boolean> = {valid: true}): void {
    input.value = value;
    input.validity = validity;
    listeners.input({target: input});
}

// The browser telling a field of the form it is invalid, as it refuses the submit. True when the bubble was called off.
function refuse(input: StubField): boolean {
    let calledOff = false;
    listeners.invalid({target: input, preventDefault() { calledOff = true; }});
    return calledOff;
}

function fresh(): void {
    hostSend({type: "navigate"});
    elapse(60_000);
    sent = [];
}

function taken(): string[] {
    const said = sent;
    sent = [];
    return said;
}

// A field that waits: the frame the next action carries says the number field held text back.
fresh();
let weight = field("number", waits);
typeUnreadable(weight);
hostSend({id: "save", type: "click"});
const heldBackBeforeAClick = taken();

// A number it can read says nothing of the kind, and neither does a text field whatever its validity says.
fresh();
weight = field("number", waits);
type(weight, "12");
const text = field("text", {"data-rask-on-change": "name", "data-rask-bind-on": "action"});
typeUnreadable(text);
hostSend({id: "save", type: "click"});
const readableSaysNothing = taken();

// A date and a time half typed.
fresh();
const day = field("date", {"data-rask-on-change": "day", "data-rask-bind-on": "action"});
const at = field("time", {"data-rask-on-change": "at", "data-rask-bind-on": "action"});
typeUnreadable(day);
typeUnreadable(at);
hostSend({id: "save", type: "click"});
const aDateAndATime = taken();

// A live field: the pause sends it, saying so.
fresh();
const live = field("number", {"data-rask-on-input": "live", "data-rask-debounce": "150"});
typeUnreadable(live);
elapse(150);
const liveAtItsPause = taken();

// A field bound on blur: the browser fires no `change` for it, so leaving it sends it — once.
fresh();
const blurred = field("number", {"data-rask-on-change": "blurred", "data-rask-bind-on": "blur"});
typeUnreadable(blurred);
const beforeLeaving = taken();
listeners.focusout({target: blurred});
const onLeaving = taken();
hostSend({id: "save", type: "click"});
const afterLeaving = taken();

// Leaving a field bound on blur that reads fine is the browser's `change` to report, not this.
fresh();
const fine = field("number", {"data-rask-on-change": "fine", "data-rask-bind-on": "blur"});
type(fine, "3");
listeners.focusout({target: fine});
const leavingAReadableField = taken();

// The browser refuses a submit over the one field that held text back: the bubble is called off and the
// submit goes, the field's frame ahead of it.
fresh();
let form = new StubForm();
weight = form.add(field("number", waits));
typeUnreadable(weight);
const bubbleCalledOff = refuse(weight);
const beforeItsTask = taken();
elapse(0);
const submittedAnyway = taken();

// A fraction in a whole-number field with no step of its own goes the same way, its text with it.
fresh();
form = new StubForm();
weight = form.add(field("number", waits));
type(weight, "1.5", {valid: false, stepMismatch: true});
const fractionCalledOff = refuse(weight);
elapse(0);
const fractionSubmitted = taken();

// A date half typed fires no `input`, so nothing waits for it: the refusal is what notes it.
fresh();
form = new StubForm();
const halfDay = form.add(field("date", {"data-rask-on-change": "day", "data-rask-bind-on": "action"}));
halfDay.validity = {valid: false, badInput: true};
refuse(halfDay);
elapse(0);
const aDateNobodyHeardTyped = taken();

// Two such fields are one submit.
fresh();
form = new StubForm();
weight = form.add(field("number", waits));
const height = form.add(field("number", {"data-rask-on-change": "height", "data-rask-bind-on": "action"}));
typeUnreadable(weight);
typeUnreadable(height);
refuse(weight);
refuse(height);
elapse(0);
const twoFieldsOneSubmit = form.submits;
taken();

// Beside a field the app marked required and the reader left empty, the browser still refuses: only what
// could not be read is sent, and its own bubble is called off.
fresh();
form = new StubForm();
weight = form.add(field("number", waits));
const required = form.add(field("text", {"data-rask-on-change": "name", "data-rask-bind-on": "action", required: ""}));
required.validity = {valid: false, valueMissing: true};
typeUnreadable(weight);
const besideRequiredCalledOff = refuse(weight);
const requiredLeftToTheBrowser = !refuse(required);
elapse(0);
const besideRequired = taken();
const besideRequiredSubmits = form.submits;

// A step the app wrote, a field the page does not bind, and a number out of the app's range are the browser's.
fresh();
form = new StubForm();
const stepped = form.add(field("number", {...waits, step: "5"}));
type(stepped, "7", {valid: false, stepMismatch: true});
const controlled = form.add(field("number", {"data-rask-on-change": "own"}));
controlled.validity = {valid: false, badInput: true};
const ranged = form.add(field("number", waits));
type(ranged, "99", {valid: false, rangeOverflow: true});
const leftToTheBrowser = [refuse(stepped), refuse(controlled), refuse(ranged)];
elapse(0);
const leftToTheBrowserSubmits = form.submits;

// A form whose handler takes what was posted whatever the rules say: the browser's refusal stands.
fresh();
form = new StubForm(false);
weight = form.add(field("number", waits));
typeUnreadable(weight);
const noModelCalledOff = refuse(weight);
elapse(0);
const noModelSubmits = form.submits;
const noModelSent = taken();

process.stdout.write(JSON.stringify({
    noModelCalledOff,
    noModelSubmits,
    noModelSent,
    heldBackBeforeAClick,
    readableSaysNothing,
    aDateAndATime,
    liveAtItsPause,
    beforeLeaving,
    onLeaving,
    afterLeaving,
    leavingAReadableField,
    bubbleCalledOff,
    beforeItsTask,
    submittedAnyway,
    fractionCalledOff,
    fractionSubmitted,
    aDateNobodyHeardTyped,
    twoFieldsOneSubmit,
    besideRequiredCalledOff,
    requiredLeftToTheBrowser,
    besideRequired,
    besideRequiredSubmits,
    leftToTheBrowser,
    leftToTheBrowserSubmits,
}) + "\n");
