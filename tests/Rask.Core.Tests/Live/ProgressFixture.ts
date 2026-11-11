// Script fixture for rask-progress.ts — the bar at the top of the viewport both hosts show while the reader waits:
// when it shows, when it goes, and what it is drawn with.
//
// The module's timers run on a clock this fixture turns by hand, so "149 ms later" is exact and nothing waits.
// The C# test (ProgressTests) runs this on Jint and asserts what it reports.

import {report} from "../../Rask.TestSupport/ScriptFixtures/fixture.js";
import {installStubGlobals} from "./stub-dom.js";
import {advance, installClock} from "./hand-clock.js";

interface Node {
    tagName: string;
    className: string;
    textContent: string;
    attributes: Map<string, string>;
    children: Node[];
    isConnected: boolean;
    setAttribute(name: string, value: string): void;
    removeAttribute(name: string): void;
    hasAttribute(name: string): boolean;
    appendChild(child: Node): Node;
}

function node(tagName: string): Node {
    const el: Node = {
        tagName, className: "", textContent: "", attributes: new Map(), children: [], isConnected: false,
        setAttribute: (name, value) => { el.attributes.set(name, value); },
        removeAttribute: (name) => { el.attributes.delete(name); },
        hasAttribute: (name) => el.attributes.has(name),
        appendChild(child) { child.isConnected = true; el.children.push(child); return child; },
    };
    return el;
}

const head = node("HEAD");
const html = node("HTML");
installStubGlobals({head, documentElement: html, createElement: (tag: string) => node(tag.toUpperCase())} as never);
installClock();

const {waitOn, PROGRESS_DELAY_MS} = await import("../../../src/Rask.Core/Resources/rask-progress.js");
const bar = () => html.children.find(c => c.className === "rask-pending") ?? null;
const showing = () => !!bar()?.hasAttribute("data-show");

// ---- An answer inside the delay never draws the bar ----
const fast = waitOn();
advance(PROGRESS_DELAY_MS - 1);
const fastBefore = showing();
fast();
advance(1000);
const fastNever = showing();
const fastInstalled = bar() !== null;

// ---- Two quick ones that overlap: neither waited long enough ----
const first = waitOn();
advance(100);
const second = waitOn();
advance(49);
first();
advance(100);
const overlapShown = showing();
second();
advance(1000);

// ---- A slow one shows at the delay, and goes when it is answered ----
const slow = waitOn();
advance(PROGRESS_DELAY_MS - 1);
const slowBefore = showing();
advance(1);
const slowAt = showing();
const other = waitOn();
slow();
const slowStillWaited = showing();
other();
const slowGone = showing();
// Answering one twice must not count another as answered: the bar would stay up after it.
const third = waitOn();
other();
advance(PROGRESS_DELAY_MS);
const thirdShown = showing();
third();
const twiceAnswered = showing();

const style = head.children.find(c => c.tagName === "STYLE");
report({
    delay: PROGRESS_DELAY_MS,
    fast: {before: fastBefore, never: fastNever, installed: fastInstalled},
    overlap: {shown: overlapShown},
    slow: {before: slowBefore, at: slowAt, stillWaited: slowStillWaited, gone: slowGone, thirdShown, twice: twiceAnswered},
    bar: {hidden: bar()?.attributes.get("aria-hidden") ?? null, managed: bar()?.hasAttribute("data-rask-managed") ?? false},
    css: style?.textContent ?? "",
});
