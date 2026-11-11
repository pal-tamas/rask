// Script fixture for who has the last word on whether a popover, a dialog or a <details> is open:
// rask-owned.ts ("the reader's last word"), through the real listeners of rask-events.ts and the real
// reconcile of rask-dom.ts.
//
// The host stamps a seq on every event, as the Server's does, and the fixture plays the server: `rendered`
// is a reply writing the attribute, `answered(n)` is the ack of seq n. The popover raises its `toggle` when
// the fixture says so, because the bug lived in the task between the browser acting and that event.
// The C# test (OpenStateTests) runs this on Jint and asserts what it reports.

import {report} from "../../Rask.TestSupport/ScriptFixtures/fixture.js";
import {elapse, frame, listeners} from "./stub-events.js";
import {StubPopover} from "./stub-open.js";
import {send, setHost} from "../../../src/Rask.Core/Resources/rask-host.js";
import "../../../src/Rask.Core/Resources/rask-events.js";
import "../../../src/Rask.Core/Resources/rask-dom.js";
import {answered, awaiting, drive, keepsOpen} from "../../../src/Rask.Core/Resources/rask-owned.js";

const OPEN = "data-rask-popover-open";
let seq = 0;
let sent: string[] = [];

setHost({
    send(payload: unknown): unknown {
        const event = payload as { type: string; newState?: string; seq?: number };
        event.seq = ++seq;
        sent.push(event.newState ? event.type + ":" + event.newState : event.type);
        return undefined;
    },
    inRoot: () => true,
});

function taken(): string[] {
    const said = sent;
    sent = [];
    seq = 0;
    return said;
}

function listbox(): StubPopover {
    return new StubPopover({"data-rask-on-toggle": "h3", [OPEN]: "false", popover: ""});
}

function click(): void {
    send({id: "h5", type: "click"});
}

// The reported trace. The reader opens the list (1), picks (2) and presses Escape; the reply to the opening,
// rendered before the server heard of Escape, says "open" and is acked while Escape's own `toggle` is still
// to be raised.
let list = listbox();
list.showPopover();
list.raise();
click();
list.hidePopover();
list.rendered(OPEN, "true");
answered(1);
const shutBeforeEscapeWasSent = !list.open;
list.raise();
answered(2);
const shutBeforeEscapeWasAnswered = !list.open;
list.rendered(OPEN, "false");
answered(3);
list.raise();
const reported = {shutBeforeEscapeWasSent, shutBeforeEscapeWasAnswered, shut: !list.open, sent: taken()};

// Escape is sent (2), and two replies older than it arrive: the last of them says "open".
list = listbox();
list.showPopover();
list.raise();
list.rendered(OPEN, "true");
answered(1);
list.hidePopover();
list.raise();
list.rendered(OPEN, "false");
list.rendered(OPEN, "true");
const shutUnderStaleReplies = !list.open;
list.rendered(OPEN, "false");
answered(2);
list.raise();
const stale = {shutUnderStaleReplies, shut: !list.open, sent: taken()};

// The page's answer to the reader's toggle is carried out once it is in: the reader opened a list the page
// then shut, in a reply that arrived before the ack.
list = listbox();
list.showPopover();
list.raise();
list.rendered(OPEN, "true");
list.rendered(OPEN, "false");
const openUntilAnswered = list.open;
answered(1);
list.raise();
const answer = {openUntilAnswered, shut: !list.open, sent: taken()};

// The page opens and shuts the list itself: neither toggle is news to it. The reader's next one is.
list = listbox();
list.rendered(OPEN, "true");
const openedByThePage = list.open;
list.raise();
list.rendered(OPEN, "false");
list.raise();
const shutByThePage = !list.open;
const told = taken();
list.showPopover();
list.raise();
const ours = {openedByThePage, shutByThePage, told, readers: taken()};

// The page opens the list and the reader shuts it before the browser has raised anything: one `toggle`, and
// it says "closed".
list = listbox();
list.rendered(OPEN, "true");
list.hidePopover();
list.raise();
const coalesced = taken();

// An opening a listener refused changes nothing and raises no `toggle`: a render is held for a second at most.
list = listbox();
listeners.beforetoggle({type: "beforetoggle", target: list, oldState: "closed", newState: "open"});
list.rendered(OPEN, "true");
const heldAtFirst = !list.open;
elapse(1000);
const refusedOpeningLetsGo = heldAtFirst && list.open;
list.raise();
taken();

// A popover that tells the page nothing holds nothing once its `toggle` is raised.
const silent = new StubPopover({[OPEN]: "true", popover: ""});
silent.showPopover();
silent.raise();
silent.rendered(OPEN, "false");
const silentIsDriven = !silent.open;
silent.raise();
taken();

// A <details>: its `open` attribute is its state, so a render older than the reader's toggle may not write it.
const details = new StubPopover({"data-rask-on-toggle": "h9"}, "DETAILS");
details.setAttribute("open", "");
listeners.toggle({type: "toggle", target: details, oldState: "closed", newState: "open"});
const keptOpen = keepsOpen(details as unknown as Element, "open", false) && details.hasAttribute("open");
answered(1);
const shutOnceAnswered = !details.hasAttribute("open");
listeners.toggle({type: "toggle", target: details, oldState: "open", newState: "closed"});
const detailsSent = taken();
const writtenWhenNothingIsOwed = !keepsOpen(details as unknown as Element, "open", true);
const untouchedAttribute = keepsOpen(details as unknown as Element, "class", true);

// A dialog's `close` is told however it closed: the one the runtime caused, and the reader's after it.
const dialog = new StubPopover({"data-rask-on-close": "h7", open: ""}, "DIALOG");
drive(dialog as unknown as Element, function () { dialog.removeAttribute("open"); });
listeners.close({type: "close", target: dialog});
const runtimeClose = taken();
listeners.close({type: "close", target: dialog});
const readerClose = taken();

// ----- The server's last word, carried out in the end (rask-owned.ts, `converges`) -----
// Whatever the scenarios above left behind is swept first, so each below starts from a page that agrees.
frame();
taken();

// The page shut the list while the browser was mid-transition: hidePopover() threw and the attribute never changes
// again. The next frame carries the "false" out — and the page is told nothing of it.
list = listbox();
list.rendered(OPEN, "true");
list.raise();
list.refusesNext = true;
list.rendered(OPEN, "false");
const leftOpenByTheThrow = list.open;
frame();
list.raise();
const thrown = {leftOpenByTheThrow, shut: !list.open, sent: taken()};

// The same throw, with nothing rendered after it: the page stops waiting on an answer, and the sweep that follows
// shuts the list.
list = listbox();
list.rendered(OPEN, "true");
list.raise();
frame();
const answering = awaiting();
list.refusesNext = true;
list.rendered(OPEN, "false");
list.refusesNext = true;
frame(); // the observer's own sweep finds it in transition again
const stillOpen = list.open;
answering();
frame();
const quiet = {stillOpen, shut: !list.open};
list.raise();
taken();

// The reader shut a list the page shows and the page was never told (no handler hears its toggle): theirs. No
// sweep opens it again until a render changes its mind.
const untoldList = new StubPopover({[OPEN]: "true", popover: ""});
untoldList.rendered(OPEN, "true");
untoldList.raise();
untoldList.hidePopover();
untoldList.raise();
frame();
const done = awaiting();
done();
frame();
const untoldStaysShut = !untoldList.open;
untoldList.rendered(OPEN, "false");
untoldList.rendered(OPEN, "true");
const reopenedByTheNextRender = untoldList.open;
untoldList.raise();
const untold = {untoldStaysShut, reopenedByTheNextRender, sent: taken()};

// The reader shut a list, the page heard it, and its answer still says open: the server's word wins.
list = listbox();
list.rendered(OPEN, "true");
list.raise();
list.hidePopover();
list.raise();
const shutWhileUnanswered = !list.open;
answered(1);
frame();
const heardAndKept = {shutWhileUnanswered, open: list.open, sent: taken()};
list.raise();

report({
    thrown,
    quiet,
    untold,
    heardAndKept,
    reported,
    stale,
    answer,
    ours,
    coalesced,
    refusedOpeningLetsGo,
    silentIsDriven,
    details: {keptOpen, shutOnceAnswered, sent: detailsSent, writtenWhenNothingIsOwed, untouchedAttribute},
    dialog: {runtimeClose, readerClose},
});
