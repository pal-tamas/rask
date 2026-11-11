// Script fixture for rask-first-invalid.ts — where focus goes once the render answering a submit is on the page:
// to the first invalid field when the form refused it, and nowhere when it did not or the reader has moved on.
//
// A stub form holds stub fields; each says whether it is marked invalid, whether it can take focus itself (a
// control, or a kit control's wrapper that cannot), and whether it is on screen. `form.setAttribute` stands in
// for the render: it is what moves `data-rask-refused`.
//
// The C# test (FirstInvalidTests) runs this on Jint and asserts what it reports.

import {nextTask, report} from "../../Rask.TestSupport/ScriptFixtures/fixture.js";
import {installStubGlobals} from "./stub-dom.js";

interface Field {
    id: string;
    invalid: boolean;
    focusable: boolean;
    shown: boolean;
    disabled: boolean;
    inert: boolean;
    inner: Field[];
    scrolled: ScrollIntoViewOptions | null;
    matches(selector: string): boolean;
    querySelectorAll(selector: string): Field[];
    closest(selector: string): Field | null;
    getClientRects(): unknown[];
    focus(options?: FocusOptions): void;
    scrollIntoView(options?: ScrollIntoViewOptions): void;
}

const body = {id: "body"};
const doc = {activeElement: body as unknown, body, documentElement: {id: "html"}};
let reduce = false;
const g = globalThis as unknown as {matchMedia: unknown};
g.matchMedia = (query: string) => ({matches: reduce && query === "(prefers-reduced-motion: reduce)"});
installStubGlobals(doc as never);

function field(id: string, invalid = false): Field {
    const f: Field = {
        id, invalid, focusable: true, shown: true, disabled: false, inert: false, inner: [], scrolled: null,
        matches: () => f.focusable,
        querySelectorAll: () => f.inner,
        closest: (selector) => selector === "[inert]" && f.inert ? f : null,
        getClientRects: () => f.shown ? [{}] : [],
        focus() { if (f.focusable && f.shown && !f.disabled) doc.activeElement = f; },
        scrollIntoView(options) { f.scrolled = options ?? {}; },
    };
    return f;
}

const fields: Field[] = [];
const form = {
    isConnected: true,
    attributes: new Map<string, string>(),
    getAttribute(name: string) { return form.attributes.get(name) ?? null; },
    setAttribute(name: string, value: string) { form.attributes.set(name, value); },
    querySelectorAll: (selector: string) => selector === "[aria-invalid=\"true\"]" ? fields.filter(f => f.invalid) : [],
};

const {focusFirstInvalid, submitAnswered, watchSubmit, firstInvalid} =
    await import("../../../src/Rask.Core/Resources/rask-first-invalid.js");

const pressed = field("save");
let refusals = 0;

function page(...each: Field[]): void {
    fields.length = 0;
    fields.push(...each);
    doc.activeElement = pressed;
}

// Presses save, lets the "render" say whether it refused, and reports where focus is after the next task.
async function submit(refuse: boolean, between?: () => void): Promise<string | null> {
    const watch = watchSubmit(form as never);
    if (refuse) form.setAttribute("data-rask-refused", String(++refusals));
    if (between) between();
    submitAnswered(watch);
    await nextTask();
    const now = doc.activeElement as {id: string} | null;
    return now ? now.id : null;
}

// ---- The rules refused it: the first marked field, in document order ----
const name = field("name");
const email = field("email", true);
const city = field("city", true);
page(name, email, city);
const refused = {focus: await submit(true), scrolled: email.scrolled};

// ---- The same marks, but this submit was not refused: focus stays where the press left it ----
page(name, email, city);
const notRefused = await submit(false);

// ---- Reduced motion: shown at once, not scrolled smoothly ----
reduce = true;
const still = field("still", true);
page(still);
await submit(true);
const reduced = still.scrolled;
reduce = false;

// ---- The reader moved on while it was in flight ----
const other = field("other");
page(name, email);
const movedOn = await submit(true, () => { doc.activeElement = other; });

// ---- Focus lost to the page while the form was held is not the reader moving on ----
page(name, email);
const fromBody = await submit(true, () => { doc.activeElement = body; });

// ---- Not on screen (a tab not shown), disabled, inert: passed over ----
const onHiddenTab = field("on-hidden-tab", true); onHiddenTab.shown = false;
const off = field("off", true); off.disabled = true;
const inInert = field("in-inert", true); inInert.inert = true;
const shownOne = field("shown-one", true);
page(onHiddenTab, off, inInert, shownOne);
const passedOver = await submit(true);

// ---- A tab the refusal opens is shown after the render, before the next task: its field is the one ----
const onRefusedTab = field("on-refused-tab", true); onRefusedTab.shown = false;
const later = field("later", true);
page(onRefusedTab, later);
const tabShown = await submit(true, () => { setTimeout(() => { onRefusedTab.shown = true; }, 0); });

// ---- A kit control marked on its wrapper: the first focusable thing inside it ----
const wrapper = field("select-wrapper", true); wrapper.focusable = false;
const hiddenInput = field("select-value"); hiddenInput.shown = false;
const trigger = field("select-trigger");
wrapper.inner = [hiddenInput, trigger];
page(name, wrapper);
const wrapped = await submit(true);

// ---- Nothing that can take focus: nothing is focused, and nothing throws ----
const lost = field("lost", true); lost.shown = false;
page(lost);
const nothing = await submit(true);
const noneFirst = firstInvalid(form as never);

// ---- A form that went away with its page ----
page(name, email);
const watch = watchSubmit(form as never);
form.setAttribute("data-rask-refused", String(++refusals));
form.isConnected = false;
const gone = {took: focusFirstInvalid(watch), focus: (doc.activeElement as {id: string}).id};
form.isConnected = true;

report({refused, notRefused, reduced, movedOn, fromBody, passedOver, tabShown, wrapped, nothing, noneFirst, gone});
