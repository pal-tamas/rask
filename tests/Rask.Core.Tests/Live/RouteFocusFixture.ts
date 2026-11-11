// Script fixture for rask-route-focus.ts — what a navigation's page does for a reader who cannot see it arrive,
// on both hosts: where keyboard focus goes, what the live region says, and what is scrolled.
//
// The stub page is a handful of elements the scenarios add and remove. `focus()` on one takes only when the
// element can take focus — a control, or anything with a tabindex — as a browser's does. The announcement is
// written after a short pause, so the clock is turned by hand.
//
// The C# test (RouteFocusTests) runs this on Jint and asserts what it reports.

import {report} from "../../Rask.TestSupport/ScriptFixtures/fixture.js";
import {installStubGlobals} from "./stub-dom.js";
import {advance, installClock} from "./hand-clock.js";

interface El {
    id: string;
    tagName: string;
    textContent: string;
    className: string;
    style: {cssText: string};
    attributes: Map<string, string>;
    isConnected: boolean;
    autofocus: boolean;
    disabled: boolean;
    scrolledTo: boolean;
    blurred: (() => void)[];
    setAttribute(name: string, value: string): void;
    getAttribute(name: string): string | null;
    hasAttribute(name: string): boolean;
    removeAttribute(name: string): void;
    closest(selector: string): El | null;
    getClientRects(): unknown[];
    focus(options?: {preventScroll?: boolean}): void;
    scrollIntoView(): void;
    addEventListener(type: string, heard: () => void): void;
    appendChild(child: El): El;
}

const page: El[] = [];
const doc = {
    title: "",
    activeElement: null as El | null,
    body: null as El | null,
    documentElement: null as unknown as El,
    createElement: (tag: string) => el("", tag.toUpperCase()),
    querySelector(selector: string): El | null {
        if (selector === "main, [role=main]") {
            return page.find(e => e.tagName === "MAIN" || e.getAttribute("role") === "main") ?? null;
        }
        if (selector === "h1") return page.find(e => e.tagName === "H1") ?? null;
        // As a browser's: `#1` is not a selector.
        if (/^#\d/.test(selector)) throw new SyntaxError(`'${selector}' is not a valid selector.`);
        if (selector.startsWith("#")) return page.find(e => e.id === selector.slice(1)) ?? null;
        return null;
    },
    getElementById: (id: string) => page.find(e => e.id === id) ?? null,
    querySelectorAll: (selector: string) => selector === "[autofocus]" ? page.filter(e => e.autofocus) : [],
    addEventListener: () => {},
};

function el(id: string, tagName: string): El {
    const e: El = {
        id, tagName, textContent: "", className: "", style: {cssText: ""}, attributes: new Map(), isConnected: true,
        autofocus: false, disabled: false, scrolledTo: false, blurred: [],
        setAttribute: (name, value) => { e.attributes.set(name, value); },
        getAttribute: (name) => e.attributes.get(name) ?? null,
        hasAttribute: (name) => e.attributes.has(name),
        removeAttribute: (name) => { e.attributes.delete(name); },
        closest: () => null,
        getClientRects: () => [{}],
        focus() {
            if (!/^(INPUT|BUTTON|SELECT|TEXTAREA)$/.test(e.tagName) && !e.hasAttribute("tabindex")) return;
            const was = doc.activeElement;
            doc.activeElement = e;
            if (was && was !== e) was.blurred.splice(0).forEach(heard => heard());
        },
        scrollIntoView: () => { e.scrolledTo = true; },
        addEventListener: (type, heard) => { if (type === "blur") e.blurred.push(heard); },
        appendChild: (child) => child,
    };
    return e;
}

doc.documentElement = el("", "HTML");
doc.body = el("", "BODY");
let scrolledTop = 0;
const g = globalThis as unknown as {scrollTo: unknown; location: unknown};
g.scrollTo = () => { scrolledTop++; };
g.location = {pathname: "/orders/7"};
installStubGlobals(doc as never);
installClock();

const {navigationShown, backOrForward, installRouteAnnouncer} =
    await import("../../../src/Rask.Core/Resources/rask-route-focus.js");
const region = installRouteAnnouncer() as unknown as El;
const regionAttrs = {
    live: region.getAttribute("aria-live"),
    atomic: region.getAttribute("aria-atomic"),
    managed: region.hasAttribute("data-rask-managed"),
    className: region.className,
};

const pressed = el("pressed", "BUTTON");

function show(title: string, ...elements: El[]): void {
    page.length = 0;
    page.push(...elements);
    doc.title = title;
    doc.activeElement = pressed;
    scrolledTop = 0;
}

const focus = () => doc.activeElement ? doc.activeElement.id : null;

// What the region says the moment the page is shown, and once the pause is over.
function heard(run: () => void): {at: string; after: string} {
    region.textContent = "unchanged";
    run();
    const at = region.textContent;
    advance(100);
    return {at, after: region.textContent};
}

// ---- Forward, to a page with a <main> ----
const main = el("main", "MAIN");
show("Orders | Shop", el("nav", "NAV"), main, el("title", "H1"));
const forwardHeard = heard(() => navigationShown({action: "push"}, ""));
const forward = {
    focus: focus(), heard: forwardHeard, scrolledTop,
    tabindex: main.getAttribute("tabindex"),
};
pressed.focus();
const forwardAfterBlur = main.getAttribute("tabindex");

// ---- A <main> that is a tab stop of its own keeps it ----
const stop = el("main", "MAIN");
stop.setAttribute("tabindex", "0");
show("Orders | Shop", stop);
navigationShown({action: "push"}, "");
const ownFocus = focus();
pressed.focus();
const ownTabindex = stop.getAttribute("tabindex");

// ---- No <main>: its heading ----
show("Plain | Shop", el("p", "P"), el("heading", "H1"));
navigationShown({action: "push"}, "");
const headingFocus = focus();

// ---- Neither: focus stays, the page is still announced ----
show("Bare | Shop", el("p", "P"));
const bareHeard = heard(() => navigationShown({action: "push"}, ""));
const bare = {focus: focus(), heard: bareHeard};

// ---- No title: the address is announced ----
show("", el("main", "MAIN"));
const untitled = heard(() => navigationShown({action: "push"}, ""));

// ---- At a #fragment ----
const part = el("shipping", "SECTION");
show("Orders | Shop", el("main", "MAIN"), part);
navigationShown({action: "push"}, "#shipping");
const fragment = {focus: focus(), scrolledTo: part.scrolledTo, scrolledTop};

show("Orders | Shop", el("main", "MAIN"));
navigationShown({action: "push"}, "#gone");
const missing = {focus: focus(), scrolledTop};

// A fragment that is no selector at all.
show("Orders | Shop", el("1", "SECTION"), el("main", "MAIN"));
navigationShown({action: "push"}, "#1");
const digits = focus();

// ---- A page with an autofocus field: the field, not the page ----
const name = el("name", "INPUT");
name.autofocus = true;
const formMain = el("main", "MAIN");
show("New order | Shop", formMain, name);
navigationShown({action: "push"}, "");
const autofocused = {focus: focus(), tabindex: formMain.getAttribute("tabindex")};

// ---- The same page again ----
const again = el("main", "MAIN");
show("Orders | Shop", again);
navigationShown({action: "push"}, "");
pressed.focus();
navigationShown({action: "push"}, "");
const samePage = focus();
advance(100);

// ---- Two navigations inside the pause: only the newer page is said ----
show("First | Shop", el("main", "MAIN"));
region.textContent = "unchanged";
navigationShown({action: "push"}, "");
advance(20);
show("Second | Shop", el("main", "MAIN"));
navigationShown({action: "push"}, "");
advance(40);
const quickMiddle = region.textContent;
advance(100);
const quick = {middle: quickMiddle, after: region.textContent};

// ---- A replaced address nobody went Back for: the same page, nothing said ----
show("Orders | Shop", el("main", "MAIN"));
const replaced = {heard: heard(() => navigationShown({action: "replace"}, "")), focus: focus(), scrolledTop};

// ---- Back: announced, focus and scroll left alone; a render in between does not use it up ----
show("Orders | Shop", el("main", "MAIN"));
backOrForward();
const between = heard(() => navigationShown(null, ""));
const backHeard = heard(() => navigationShown({action: "replace"}, ""));
const back = {between, heard: backHeard, focus: focus(), scrolledTop};
const afterBack = heard(() => navigationShown({action: "replace"}, ""));

// ---- A frame with no history: nothing ----
show("Orders | Shop", el("main", "MAIN"));
const render = {heard: heard(() => navigationShown(undefined, "")), focus: focus(), scrolledTop};

report({
    region: regionAttrs,
    forward, forwardAfterBlur,
    own: {focus: ownFocus, tabindex: ownTabindex},
    headingFocus,
    bare,
    untitled,
    fragment, missing, digits,
    autofocused,
    samePage,
    quick,
    replaced,
    back, afterBack,
    render,
});
