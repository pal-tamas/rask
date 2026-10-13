// Node-driven fixture for rask-input.ts and rask-events.ts — a bound control that waits for the next action
// (`data-rask-bind-on="action"`, what a plain `.Bind(…)` renders), and everything that counts as one.
//
// The host here does what both real hosts do at the top of their `send`: it asks rask-input to send what waits
// first (sendTypedFirst). So what this fixture records is the order the frames would leave in. Time passes when
// the fixture says so (stub-events.ts). The C# test (DeferredBindClientTests) runs this in a node subprocess
// and asserts the JSON line.

import {autofocused, elapse, frame, listeners, StubField} from "./stub-events.js";
import {focusArrived} from "../../../src/Rask.Core/Resources/rask-autofocus.js";
import {setHost} from "../../../src/Rask.Core/Resources/rask-host.js";
import {changeSent, remember, sendTypedFirst, waitsForAction} from "../../../src/Rask.Core/Resources/rask-input.js";
import {listen, replayMissed, seam, tell, unasked} from "../../../src/Rask.Core/Resources/rask-owned.js";
import "../../../src/Rask.Core/Resources/rask-events.js";

let sent: string[] = [];
let owed: (() => void)[] = [];
let answers = false;

function hostSend(payload: unknown): unknown {
    sendTypedFirst(payload);
    const event = payload as { id?: string; type: string; value?: string; values?: string[]; unasked?: boolean };
    const said = event.values ? event.values.join("+") : event.value;
    // An event the runtime raised by itself says so on the wire: written here with a `~` after it.
    sent.push((said === undefined ? event.type + ":" + (event.id ?? "") : event.type + ":" + said) + (event.unasked ? "~" : ""));
    return answers ? new Promise<void>(function (answer) { owed.push(answer); }) : undefined;
}

setHost({send: hostSend, inRoot: () => true});

function type(field: StubField, value: string, isComposing = false): void {
    field.value = value;
    listeners.input({target: field, isComposing});
}

function leave(field: StubField): void {
    listeners.change({target: field});
}

function choose(field: StubField, checked: boolean): void {
    field.checked = checked;
    listeners.input({target: field});
    listeners.change({target: field});
}

// A field on screen that hears its own focus, as the browser raises it when a script focuses the field.
class OnScreen extends StubField {
    getClientRects(): unknown[] { return [this]; }
    focus(): void { listeners.focus({target: this}); }
}

// Each case starts with nothing waiting from the one before it.
function fresh(): void {
    hostSend({type: "navigate"});
    elapse(60_000);
    frame();
    sent = [];
    owed = [];
}

function waiting(id: string, extra: Record<string, string> = {}): StubField {
    return new StubField({"data-rask-on-change": id, "data-rask-bind-on": "action", ...extra});
}

function taken(): string[] {
    const said = sent;
    sent = [];
    return said;
}

// Typed into, paused over and left: not a word. Its own `change` is the host's to skip.
fresh();
let name = waiting("name");
type(name, "A");
type(name, "At");
elapse(5000);
frame();
leave(name);
const hostSkipsItsChange = waitsForAction(name as unknown as Element);
const typedAndLeft = taken();

// A click: the value first, then the click. A second click finds nothing left to send.
hostSend({id: "save", type: "click"});
const beforeAClick = taken();
hostSend({id: "save", type: "click"});
const clickedAgain = taken();

// The `change` the browser fires later for a value that already went is not news.
const browsersChangeAfterwards = changeSent(name as unknown as Element);

// A submit — which is also what Enter in a field raises — and a navigation.
fresh();
name = waiting("name");
type(name, "Bea");
hostSend({id: "form", type: "submit"});
const beforeASubmit = taken();
type(name, "Cy");
hostSend({type: "navigate"});
const beforeANavigation = taken();

// Three fields: in the order they were last touched, each once.
fresh();
const first = waiting("first");
const second = waiting("second");
const third = waiting("third");
type(first, "1");
type(second, "2");
type(third, "3");
type(first, "1!");
hostSend({id: "save", type: "click"});
const inTheOrderLastTouched = taken();

// A key a handler hears goes after the value. A key its data-rask-keys leaves out is not sent, and sends nothing.
fresh();
name = waiting("name");
const combo = new StubField({"data-rask-on-keydown": "keys", "data-rask-keys": "Enter Escape"});
type(name, "Dee");
listeners.keydown({target: combo, key: "a"});
const aKeyNobodyHears = taken();
listeners.keydown({target: combo, key: "Enter"});
const beforeAHandledKey = taken();

// A key listed as "Backspace:empty" is heard only from a field that holds nothing: in a pillbox's input it takes
// the last pill off there, and deletes a letter everywhere else.
fresh();
const pills = new StubField({"data-rask-on-keydown": "pills", "data-rask-keys": "Enter Backspace:empty"});
pills.value = "be";
listeners.keydown({target: pills, key: "Backspace"});
const backspaceOverText = taken();
pills.value = "";
listeners.keydown({target: pills, key: "Backspace"});
const backspaceInAnEmptyField = taken();

// Every other event the READER raises is sent by the same `send`: one from MDN's table (dblclick), a pointer
// crossing an edge, the start of a drag, a popover they opened or shut.
fresh();
name = waiting("name");
type(name, "Eve");
listeners.dblclick({target: new StubField({"data-rask-on-dblclick": "twice"})});
const beforeATableEvent = taken();
type(name, "Fay");
listeners.mouseover({target: new StubField({"data-rask-on-mouseenter": "over"}), relatedTarget: null});
const beforeAPointerEntering = taken();
type(name, "Gus");
listeners.dragstart({target: new StubField({"data-rask-on-dragstart": "drag"})});
const beforeADrag = taken();
type(name, "Ida");
listeners.toggle({target: new StubField({"data-rask-on-toggle": "opened"})});
const beforeAToggle = taken();

// What the runtime does BY ITSELF is nobody's next action: it is sent alone, marked, and what waits goes on
// waiting for the reader. A toast whose time is up presses its own close button; a chart announces the box it
// was measured at on its bound field; the browser says a box scrolled, a video played on, an animation ended,
// an image arrived; the server is answered.
fresh();
name = waiting("name");
type(name, "Hal");
unasked(function () { hostSend({id: "close", type: "click"}); });
const aToastsOwnPress = taken();
const measured = new StubField({"data-rask-on-input": "size", "data-rask-on-change": "sized"});
unasked(function () { type(measured, "640x480"); });
const aMeasuredBox = taken();
listeners.scroll({target: new StubField({"data-rask-on-scroll": "scrolled"})});
const scrollBeforeItsFrame = taken();
frame();
const aScroll = taken();
listeners.timeupdate({target: new StubField({"data-rask-on-timeupdate": "playing"})});
const mediaPlayingOn = taken();
listeners.animationend({target: new StubField({"data-rask-on-animationend": "faded"})});
const anAnimationEnding = taken();
listeners.load({target: new StubField({"data-rask-on-load": "arrived"})});
const anImageArriving = taken();
unasked(function () { hostSend({id: "7", type: "jsResult"}); });
const anInteropReply = taken();
autofocused.push(new OnScreen({"data-rask-on-focus": "arrived", autofocus: ""}));
focusArrived();
const aFieldARenderFocused = taken();
hostSend({id: "save", type: "click"});
const stillSentWithTheAction = taken();

// A control the page draws hears the reader only to draw itself again: a list opened, the row the pointer is
// over, an arrow key, what is typed into its search, the pick of a list that waits. The element names those
// handlers (data-rask-draws), and each is sent alone and marked, as what the runtime does by itself is.
fresh();
name = waiting("name");
type(name, "Liv");
const row = new StubField({"data-rask-on-mouseenter": "row", "data-rask-on-click": "pick", "data-rask-draws": "mouseenter click"});
const list = new StubField({"data-rask-on-toggle": "list", "data-rask-on-keydown": "keys", "data-rask-draws": "toggle keydown"});
const searched = new StubField({"data-rask-on-input": "search", "data-rask-draws": "input"});
const month = new StubField({"data-rask-on-input": "paged", "data-rask-on-change": "month", "data-rask-draws": "input change"});
listeners.toggle({target: list});
listeners.mouseover({target: row, relatedTarget: null});
listeners.keydown({target: list, key: "ArrowDown"});
type(searched, "Ad");
frame();
type(month, "3");
tell(row as unknown as Element, "click", function () { return hostSend({id: "pick", type: "click"}); });
tell(month as unknown as Element, "change", function () { return hostSend({id: "month", type: "change"}); });
const whatOnlyDraws = taken();

// A handler the element does not name is an action like any other, on the same element.
const mixed = new StubField({"data-rask-on-toggle": "opened", "data-rask-on-keydown": "keys", "data-rask-draws": "keydown"});
listeners.toggle({target: mixed});
const aHandlerNotNamed = taken();

// What waited through all of that is not lost: it goes with the next action, ahead of it — and so does what
// was typed into the list's search and has not left yet.
type(name, "Liv!");
listeners.toggle({target: list});
type(searched, "AdB");
hostSend({id: "save", type: "click"});
const afterTheDrawing = taken();

// A press the hooks missed while their bundle was on its way is handed to them when it arrives, and what a
// hook then sends for it is the reader's as much as it was: the value goes ahead.
fresh();
name = waiting("name");
type(name, "Ian");
listen("pointerdown", function () { hostSend({id: "row", type: "click"}); });
seam.missed = [{type: "pointerdown"} as unknown as Event];
replayMissed();
const aReplayedPress = taken();

// Another control that IS sent at once carries what was typed before it: a live field's pause, a field sent
// at every key, a field bound on blur as it is left.
fresh();
name = waiting("name");
const live = new StubField({"data-rask-on-input": "live", "data-rask-debounce": "150"});
type(name, "Jo");
type(live, "x");
elapse(149);
const liveBeforeItsPause = taken();
elapse(1);
const beforeALiveFieldsPause = taken();
elapse(1000);
const liveSaysNoMore = taken();
const everyKey = new StubField({"data-rask-on-input": "key", "data-rask-on-change": "left"});
type(name, "Kay");
type(everyKey, "y");
const beforeAFieldSentAtEveryKey = taken();
const framed = new StubField({"data-rask-on-input": "framed"});
type(name, "Lee");
type(framed, "z");
frame();
const beforeAFieldSentOnceAFrame = taken();

// Chosen, not typed: a checkbox and a select say `change` where a text field says `input`. They wait too.
fresh();
const box = waiting("agree", {type: "checkbox"});
choose(box, true);
const select = waiting("plan");
select.tagName = "SELECT";
select.value = "pro";
leave(select);
const chosenAndLeft = taken();
hostSend({id: "save", type: "click"});
const choicesBeforeAClick = taken();

// A radio group: the one checked speaks for the group, whichever was pressed last before it.
fresh();
const cash = waiting("pay", {type: "radio", name: "pay"});
const card = waiting("pay", {type: "radio", name: "pay"});
cash.value = "cash";
card.value = "card";
choose(cash, true);
cash.checked = false;
choose(card, true);
card.checked = false;
choose(cash, true);
hostSend({id: "save", type: "click"});
const theRadioChecked = taken();

// A message under a field that waits: the first keystroke says so, alone — no value rides ahead of it.
fresh();
name = waiting("name", {"data-rask-on-edit": "stale"});
type(name, "M");
type(name, "Mo");
const firstKeystrokeOfACorrection = taken();
hostSend({id: "save", type: "click"});
const thenTheCorrection = taken();

// A field that left the page before the action has nothing to say.
fresh();
name = waiting("name");
type(name, "Oz");
name.isConnected = false;
hostSend({id: "save", type: "click"});
const goneBeforeTheAction = taken();

// A field the redeploy restore filled in again waits as it did before the reload.
fresh();
name = waiting("name");
name.value = "Pat";
remember(name as unknown as HTMLInputElement);
const restored = taken();
hostSend({id: "save", type: "click"});
const restoredBeforeAClick = taken();

// A character being composed in a live field counts no pause, and the end of the composition starts one.
fresh();
const composed = new StubField({"data-rask-on-input": "live", "data-rask-debounce": "150"});
type(composed, "あ", true);
elapse(1000);
const whileComposing = taken();
listeners.compositionend({target: composed});
elapse(150);
const afterComposing = taken();

// A host that says when it has answered (WASM): what is typed into a field sent at every key while .NET still
// owes it an answer is held — and goes ahead of the click with what waited, each once.
fresh();
answers = true;
name = waiting("name");
const held = new StubField({"data-rask-on-input": "key", "data-rask-on-change": "left"});
type(held, "a");
type(held, "ab");
type(name, "Quy");
const heldWhileOwed = taken();
hostSend({id: "save", type: "click"});
const heldBeforeAClick = taken();
owed.splice(0).forEach(function (answer) { answer(); });
await Promise.resolve();
await Promise.resolve();
const afterTheAnswers = taken();
answers = false;

process.stdout.write(JSON.stringify({
    hostSkipsItsChange,
    typedAndLeft,
    beforeAClick,
    clickedAgain,
    browsersChangeAfterwards,
    beforeASubmit,
    beforeANavigation,
    inTheOrderLastTouched,
    aKeyNobodyHears,
    beforeAHandledKey,
    backspaceOverText,
    backspaceInAnEmptyField,
    beforeATableEvent,
    beforeAPointerEntering,
    beforeADrag,
    beforeAToggle,
    whatOnlyDraws,
    aHandlerNotNamed,
    afterTheDrawing,
    aToastsOwnPress,
    aMeasuredBox,
    scrollBeforeItsFrame,
    aScroll,
    mediaPlayingOn,
    anAnimationEnding,
    anImageArriving,
    aReplayedPress,
    liveBeforeItsPause,
    beforeALiveFieldsPause,
    liveSaysNoMore,
    beforeAFieldSentAtEveryKey,
    beforeAFieldSentOnceAFrame,
    chosenAndLeft,
    choicesBeforeAClick,
    theRadioChecked,
    firstKeystrokeOfACorrection,
    thenTheCorrection,
    anInteropReply,
    aFieldARenderFocused,
    stillSentWithTheAction,
    goneBeforeTheAction,
    restored,
    restoredBeforeAClick,
    whileComposing,
    afterComposing,
    heldWhileOwed,
    heldBeforeAClick,
    afterTheAnswers,
}) + "\n");
