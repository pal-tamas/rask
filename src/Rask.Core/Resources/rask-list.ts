// A list of options the reader moves about in the browser (data-rask-list): a listbox under a button, a
// combobox, an autocomplete, a pillbox.
//
// Which row is lit, whether the list is open and which rows a search leaves are nothing a page needs to hear:
// held in a component's state they were a round trip per row the pointer crossed, per arrow, per letter. Here
// they are attributes this module writes and holds against the next render (rask-owned.ts), as a menu's are
// (rask-menu.ts). The page renders the rows and hears a PICK — the press on the row itself, so no cursor of
// the page's own names it — and, where it asked to, what is typed.
//
// THE MARKUP. `data-rask-list="<words>"` on the control's root, which holds
//   * what opens the list and takes its keys, saying `aria-haspopup="listbox"`: a button, a text input, or an
//     element around an input (a pillbox's trigger). The one that is `role="combobox"` — the search field
//     inside the list when there is one — names the lit row with `aria-activedescendant`;
//   * one `[popover]`, which is or holds the `[role=listbox]`;
//   * the rows, `[role=option]`, each with an id. `aria-disabled="true"` is a row the cursor steps over.
// and the words say what kind of list it is:
//   multiple  a pick leaves the list open, and empties what was typed unless `stay` says otherwise;
//   filter    typing hides the rows that do not hold the text (without it the page answers the search);
//   strict    a closed text input says its answer again: the `value` it was rendered with;
//   clears    a closed text input is emptied;
//   text      the answer IS the input's text: a pick writes the row's words into it and announces them;
//   top       after a pick emptied the search the cursor goes back to the first row.
// A text input the page listens to (`data-rask-on-input`, or `data-rask-on-pause` for a search that waits) is
// told what this module writes into it as it is told what the reader types — once, and only when the page
// thinks otherwise: a search it never heard is dropped unsaid, the pause being counted for it called off.
// Read on the rows: `data-rask-list-text` (the words a row is found by, where they are not its text),
// `-keywords` (more words to find it by), `-says` (what a strict input says once it is picked), `-keep` (a row
// no search hides), `data-rask-list-create="<min length>"` (the row that offers to make an option of what
// was typed: no value, always there), `-empty` (shown while no row is), `-group="<rows>"` (a heading over
// that many rows, hidden with them) and, on a pill, `-remove` (pressed by Backspace in an empty input).
//
// THE RULES are Flux UI's, measured on its live select, autocomplete and pillbox pages (2026-10-13), where an
// open list sends its server nothing:
//   * opening puts the cursor on the first picked row, or the first row; typing puts it on the first row left;
//   * the arrows go a row at a time, over what is disabled or hidden, and stop at either end; with no row lit,
//     ArrowDown lands on the first and ArrowUp on the last; Home, End and the page keys are nobody's;
//   * the pointer lights the row it ENTERS — not one that came under a pointer at rest — and the arrows go on
//     from there; a row the pointer lit goes dark when the pointer leaves the list, one the keys lit stays;
//   * Enter picks the lit row, and nothing when none is; Tab closes; letters on a list with no text field
//     jump to the next row that starts with them, and on a CLOSED one pick it;
//   * with focus on the list itself (a row was pressed and the list stayed open) the keys do nothing.

import {reveal} from "./rask-focus.js";
import {stepFrom, typeAhead} from "./rask-menu-keys.js";
import {disown, isShown, listen, near, own, page, seam, setShown} from "./rask-owned.js";

const ROOT = "[data-rask-list]";
const CREATE = "data-rask-list-create";
const OPTION = "[role=option],[" + CREATE + "]";
const TRIGGER = "[aria-haspopup=listbox]";
// What an open list keeps from the page behind it and from a form around it.
const CONTAIN = ["Enter", "ArrowUp", "ArrowDown", "Home", "End", "PageUp", "PageDown"];
const TYPE_AHEAD_MS = 500;

interface State {
    /** The lit row. */
    cursor: HTMLElement | null;
    /** Its id: a render may put another element in its place. */
    id: string;
    /** The pointer lit it. */
    pointed: boolean;
    /** Nobody chose it: it is where opening, or typing, put the cursor. */
    auto: boolean;
    /** The pointer left the list: no row is lit until a key or the pointer says one. */
    none: boolean;
    /** Something was typed: a cursor that loses its row starts again from the top, not from the picked row. */
    top: boolean;
    /** The text input says what is being typed, not the answer. */
    typing: boolean;
    /** The pointer picked in a list that stayed open: a row that comes under it at rest counts. */
    acts: boolean;
    /** What a strict input was told to say at the last pick, and the `value` it was rendered with then. */
    said: string | null;
    over: string | null;
    /** Whether the list was showing when the press in flight began, and whether that press was on the control. */
    shown: boolean;
    pressed: boolean;
    /** The list shut under a press on its own control: what it held is kept until the click has decided. */
    resumes: boolean;
    /** What the page last heard the text input say, and whether it has heard anything typed since the input last came to rest. */
    heard: string;
    searched: boolean;
    /** A row was just picked: the input goes on to say something new. */
    anew: boolean;
}

const states = new WeakMap<Element, State>();
// The lists that are open: the only ones a render can leave with a row unlit or unhidden.
const open = new Set<Element>();
let watcher: MutationObserver | null = null;
// Where the pointer last MOVED to. A row that slides under a pointer at rest is entered at this very point.
let pointerX = NaN;
let pointerY = NaN;
let typed = "";
let typedAt = 0;
// This module is writing into a text input: not the reader typing.
let saying = false;

function stateOf(root: Element): State {
    let state = states.get(root);
    if (!state) {
        states.set(root, state = {
            cursor: null, id: "", pointed: false, auto: false, none: false, top: false, typing: false, acts: false,
            said: null, over: null, shown: false, pressed: false, resumes: false, heard: "", searched: false, anew: false,
        });
    }
    return state;
}

function has(root: Element, word: string): boolean {
    return (" " + root.getAttribute("data-rask-list") + " ").indexOf(" " + word + " ") >= 0;
}

function popupOf(root: Element): HTMLElement | null {
    return root.querySelector<HTMLElement>("[popover]");
}

function rowsOf(root: Element): HTMLElement[] {
    const popup = popupOf(root);
    return popup ? Array.prototype.slice.call(popup.querySelectorAll(OPTION)) : [];
}

// What names the lit row: the search field inside the list, or the combobox that opens it.
function fieldOf(root: Element): HTMLElement | null {
    const popup = popupOf(root);
    return (popup && popup.querySelector<HTMLElement>("input[role=combobox]")) || root.querySelector<HTMLElement>("[role=combobox]");
}

// The text input the list is searched from, when it has one.
function inputOf(root: Element): HTMLInputElement | null {
    const field = fieldOf(root);
    return field instanceof HTMLInputElement ? field : null;
}

function isOff(row: Element): boolean {
    return row.getAttribute("aria-disabled") === "true";
}

function skips(row: Element): boolean {
    return isOff(row) || row.hasAttribute("data-hidden");
}

// An attribute this module writes on a rendered element and holds against the next render.
function mark(el: Element, name: string, value: string | null): void {
    if (value === null) {
        if (el.hasAttribute(name)) {
            el.removeAttribute(name);
        }
        disown(el, name);
        return;
    }
    own(el, name);
    if (el.getAttribute(name) !== value) {
        el.setAttribute(name, value);
    }
}

// The same for an attribute a render may carry too: held either way, so a render cannot put its own back.
function hide(el: Element, hidden: boolean): void {
    own(el, "data-hidden");
    if (hidden !== el.hasAttribute("data-hidden")) {
        if (hidden) el.setAttribute("data-hidden", ""); else el.removeAttribute("data-hidden");
    }
}

/** Text as a search reads it: whatever the case or the accents. Exported for the fixture. */
export function fold(text: string): string {
    return text.normalize("NFD").replace(/[̀-ͯ]/g, "").toLocaleLowerCase();
}

function words(el: Element): string {
    return (el.textContent || "").replace(/\s+/g, " ").trim();
}

function labelOf(row: Element): string {
    const said = row.getAttribute("data-rask-list-text");
    return said === null ? words(row) : said;
}

/**
 * Which rows a search for `text` leaves, and whether the create row is offered. `rows` are the options in
 * order: their words, more words to find them by, and whether no search hides them. The create row is
 * offered once the text is `min` long and names no option already there; `min` null is no create row, and
 * a negative one a create row that is always there. Exported for the fixture.
 */
export function filtered(
    text: string, rows: readonly {label: string; keywords: string | null; keep: boolean}[], min: number | null,
): {hidden: boolean[]; create: boolean} {
    const needle = fold(text);
    let exact = false;
    const hidden = rows.map(function (row) {
        const label = fold(row.label);
        exact = exact || label === needle;
        return needle !== "" && !row.keep && label.indexOf(needle) < 0
            && (row.keywords === null || fold(row.keywords).indexOf(needle) < 0);
    });
    return {hidden, create: min !== null && (min < 0 || (text.length >= Math.max(min, 1) && !exact))};
}

// What the list is searched for now: the search field's text, or what was typed over a combobox's answer.
function needle(root: Element, state: State): string {
    const input = inputOf(root);
    const popup = popupOf(root);
    if (!input || !popup) {
        return "";
    }
    return popup.contains(input) || state.typing ? input.value : "";
}

function name(root: Element, state: State): void {
    const field = fieldOf(root);
    if (field) {
        const row = state.cursor;
        mark(field, "aria-activedescendant", row && row.id && open.has(root) ? row.id : null);
    }
}

// Lights `row` and no other, and names it.
function light(root: Element, row: HTMLElement | null, pointed: boolean = false): void {
    const state = stateOf(root);
    const rows = rowsOf(root);
    for (const each of rows) {
        mark(each, "data-active", each === row ? "" : null);
    }
    state.cursor = row;
    state.id = row ? row.id : "";
    state.pointed = !!row && pointed;
    state.auto = false;
    if (row) {
        state.none = false;
    }
    name(root, state);
}

// Where the cursor is after the rows changed — a search, a render: where it was while it still can be, and
// otherwise back to the start. A render's rows (`again`) move a cursor nobody chose back to the start as well:
// a search the page answered puts it on the first row of the answer, not on the row it happened to be on.
function place(root: Element, again: boolean = false): void {
    const state = stateOf(root);
    const rows = rowsOf(root);
    let at = state.cursor;
    if (at && !at.isConnected && state.id) {
        // A render put another element in the row's place.
        const again = page!.getElementById(state.id);
        at = again && rows.indexOf(again) >= 0 ? again : null;
    }
    if (at && rows.indexOf(at) >= 0 && !skips(at) && !(again && state.auto)) {
        const auto = state.auto;
        light(root, at, state.pointed);
        state.auto = auto;
        return;
    }
    if (state.none) {
        light(root, null);
        state.none = true;
        return;
    }
    let first: HTMLElement | null = null;
    let picked: HTMLElement | null = null;
    for (const row of rows) {
        if (skips(row)) {
            continue;
        }
        first = first || row;
        if (!picked && !state.top && row.getAttribute("aria-selected") === "true") {
            picked = row;
        }
    }
    light(root, picked || first);
    state.auto = true;
}

// Hides what the search leaves out. A list the page filters is left as rendered.
function filter(root: Element): void {
    const popup = popupOf(root);
    if (popup && has(root, "filter")) {
        const rows = rowsOf(root);
        const options = rows.filter(function (row) { return !row.hasAttribute(CREATE); });
        const create = popup.querySelector("[" + CREATE + "]");
        const min = create ? create.getAttribute(CREATE) : null;
        const result = filtered(needle(root, stateOf(root)), options.map(function (row) {
            return {label: labelOf(row), keywords: row.getAttribute("data-rask-list-keywords"), keep: row.hasAttribute("data-rask-list-keep")};
        }), min === null ? null : min === "" ? -1 : Number(min) || 1);
        let any = result.create;
        for (let i = 0; i < options.length; i++) {
            hide(options[i], result.hidden[i]);
            any = any || !result.hidden[i];
        }
        if (create) {
            hide(create, !result.create);
        }
        const empty = popup.querySelector("[data-rask-list-empty]");
        if (empty) {
            hide(empty, any);
        }
        const groups = popup.querySelectorAll("[data-rask-list-group]");
        for (let i = 0; i < groups.length; i++) {
            let left = Number(groups[i].getAttribute("data-rask-list-group")) || 0;
            let shown = false;
            for (let row = groups[i].nextElementSibling; row && left > 0; row = row.nextElementSibling, left--) {
                shown = shown || !row.hasAttribute("data-hidden");
            }
            hide(groups[i], !shown);
        }
    }
}

// …and puts the cursor back among the rows that are left.
function refilter(root: Element, again: boolean = false): void {
    filter(root);
    place(root, again);
}

// Writes into a text input as the module, not the reader, and tells the page as far as `announce` says.
function say(input: HTMLInputElement, text: string, announce: "" | "input" | "change"): void {
    saying = true;
    try {
        if (input.value !== text) {
            input.value = text;
        }
        if (announce) {
            input.dispatchEvent(new Event("input", {bubbles: true}));
        }
        if (announce === "change") {
            input.dispatchEvent(new Event("change", {bubbles: true}));
        }
    } finally {
        saying = false;
    }
}

const ON_PAUSE = "data-rask-on-pause";

// What the page has heard of `input` by now: every key where it asked for every key, and otherwise what the
// last pause told it (rask-input.ts keeps that).
function note(state: State, input: HTMLInputElement, typed: boolean): void {
    const told = input.hasAttribute(ON_PAUSE) ? seam.told && seam.told.get(input)
        : typed && input.hasAttribute("data-rask-on-input") ? input.value : undefined;
    if (told !== undefined && told !== state.heard) {
        state.heard = told;
        state.searched = true;
    }
}

// The control itself rewrites its text input — emptied with the list it searched, or back to a combobox's
// answer — and the page hears of it when it thinks otherwise, as Flux's `wire:model` on that input does
// (measured on its backend-search examples: `search` is written "" on Escape and on a click elsewhere, and
// the answer's words when a combobox goes back to saying them). An input that only came back to rest tells a
// page that heard a search since it last rested; one that says something new (`anew`: a row picked, the
// answer taken away) tells it whatever went before. A search the page never heard is dropped unsaid: the
// pause being counted for it is called off the way a character being composed calls it off.
function rest(root: Element, input: HTMLInputElement, text: string, anew: boolean = false): void {
    const state = stateOf(root);
    note(state, input, false);
    const waits = input.hasAttribute(ON_PAUSE);
    const tells = (anew || state.searched) && text !== state.heard && (waits || input.hasAttribute("data-rask-on-input"));
    state.searched = false;
    state.heard = tells ? text : state.heard;
    say(input, text, tells && !waits ? "input" : "");
    if (waits && seam.hear) {
        seam.hear(input, tells);
    }
}

// What a strict input says while its list is shut: the answer it was rendered with, or the row just picked
// until a render has said so itself.
function answer(state: State, input: HTMLInputElement): string {
    const rendered = input.getAttribute("value");
    if (state.said !== null && rendered === state.over) {
        return state.said;
    }
    state.said = null;
    return rendered || "";
}

// `data-open` and `aria-expanded`, on everything of the control that says them, in the task the list opens.
function expand(root: Element, popup: Element, shown: boolean): void {
    const said = root.querySelectorAll("[aria-expanded]");
    mark(root, "data-open", shown ? "" : null);
    mark(popup, "data-open", shown ? "" : null);
    for (let i = 0; i < said.length; i++) {
        if (popup.contains(said[i])) {
            continue;
        }
        own(said[i], "aria-expanded");
        said[i].setAttribute("aria-expanded", shown ? "true" : "false");
        mark(said[i], "data-open", shown ? "" : null);
    }
}

function watch(): void {
    if (watcher || !page || typeof MutationObserver !== "function") {
        return;
    }
    // A render changed the rows of an open list — a search answered, an option made: the new ones are hidden
    // and lit as the old ones were. Only while a list is open; nothing is watched otherwise.
    watcher = new MutationObserver(function (records) {
        open.forEach(function (root) {
            const popup = root.isConnected ? popupOf(root) : null;
            if (!popup) {
                open.delete(root);
                return;
            }
            for (const record of records) {
                if (popup.contains(record.target)) {
                    refilter(root, true);
                    break;
                }
            }
            if (stateOf(root).acts) {
                // The pointer picked and the list stayed open: a pill added to the trigger pushes the rows down
                // under a pointer at rest, and Flux's cursor is on whichever row is under it then.
                // Once the trigger has grown and the list has been placed under it again (rask-place.ts does
                // that a frame after the size changed).
                // Looked at more than once: how soon the rows have settled is the layout's to say, not ours.
                for (const after of [100, 300, 600]) {
                    setTimeout(function () { under(root); }, after);
                }
            }
        });
        unwatch();
    });
    watcher.observe(page.documentElement, {subtree: true, childList: true});
}

// Lights the row the pointer is resting on, when it is one of this list's.
function under(root: Element): void {
    const at = open.has(root) && pointerX === pointerX ? page!.elementFromPoint(pointerX, pointerY) : null;
    const row = at ? at.closest<HTMLElement>(OPTION) : null;
    if (row && row.closest(ROOT) === root && !skips(row) && stateOf(root).cursor !== row) {
        light(root, row, true);
    }
}

function unwatch(): void {
    if (watcher && open.size === 0) {
        watcher.disconnect();
        watcher = null;
    }
}

function opened(root: Element, popup: Element): void {
    const state = stateOf(root);
    open.add(root);
    watch();
    if (!state.resumes) {
        state.cursor = null;
        state.id = "";
        state.none = false;
        state.top = false;
        state.acts = false;
    }
    state.resumes = false;
    expand(root, popup, true);
    refilter(root);
}

// The list is shut for good: what was typed to search it goes, and a text input says what its kind says.
function settle(root: Element): void {
    const state = stateOf(root);
    const popup = popupOf(root);
    const input = inputOf(root);
    state.resumes = false;
    state.typing = false;
    state.top = false;
    state.acts = false;
    if (!popup) {
        return;
    }
    const anew = state.anew;
    state.anew = false;
    if (input && (popup.contains(input) || has(root, "clears"))) {
        rest(root, input, "");
    } else if (input && has(root, "strict")) {
        rest(root, input, answer(state, input), anew);
        if (page!.activeElement === input) {
            // The caret after the answer, and nothing selected: what is typed next is added to it.
            input.setSelectionRange(input.value.length, input.value.length);
        }
    }
    // A shut list hides no row: the next search starts from all of them.
    filter(root);
}

function closed(root: Element, popup: Element): void {
    const state = stateOf(root);
    open.delete(root);
    unwatch();
    expand(root, popup, false);
    const keeps = state.pressed && state.shown;
    state.pressed = false;
    if (!keeps) {
        light(root, null);
        settle(root);
        return;
    }
    // A press on the control itself shut it (the browser's light dismiss) and its click is still to come: an
    // input's reopens the list as it was, a trigger's leaves it shut.
    name(root, state);
    state.resumes = true;
    setTimeout(function () {
        if (state.resumes && !open.has(root)) {
            light(root, null);
            settle(root);
        }
    }, 0);
}

function show(root: Element): void {
    setShown(popupOf(root), true);
}

function step(root: Element, dir: 1 | -1): void {
    const state = stateOf(root);
    const rows = rowsOf(root);
    const at = state.cursor ? rows.indexOf(state.cursor) : -1;
    const from = at >= 0 ? at : dir === 1 ? -1 : rows.length;
    const to = stepFrom(rows.map(skips), from, dir);
    if (to !== from && rows[to]) {
        light(root, rows[to]);
    }
}

// The row the letters typed so far land on: counted from the cursor, or from the picked row of a shut list.
function ahead(root: Element, key: string): HTMLElement | null {
    const rows = rowsOf(root).filter(function (row) { return !row.hasAttribute(CREATE); });
    const state = stateOf(root);
    let from = state.cursor && open.has(root) ? rows.indexOf(state.cursor) : -1;
    if (!open.has(root)) {
        for (let i = 0; i < rows.length && from < 0; i++) {
            from = rows[i].getAttribute("aria-selected") === "true" ? i : -1;
        }
    }
    const now = Date.now();
    typed = now - typedAt > TYPE_AHEAD_MS ? key : typed + key;
    typedAt = now;
    const hit = typeAhead(typed, key, from, rows.map(function (row) { return skips(row) ? null : labelOf(row); }));
    return hit >= 0 ? rows[hit] : null;
}

function press(row: HTMLElement | null): void {
    if (row && !skips(row)) {
        row.click();
    }
}

// A row was pressed, by the pointer (`pointed`) or by Enter. The page hears the press itself; what the list
// does next is here.
function picked(root: Element, row: HTMLElement, pointed: boolean): void {
    const state = stateOf(root);
    const popup = popupOf(root);
    const input = inputOf(root);
    if (!popup) {
        return;
    }
    if (has(root, "text") && input) {
        // The answer is the text: written here, announced as typing is, and marked as the row last picked.
        for (const each of rowsOf(root)) {
            own(each, "aria-selected");
            each.setAttribute("aria-selected", each === row ? "true" : "false");
        }
        state.typing = false;
        say(input, labelOf(row), "change");
        setShown(popup, false);
        input.focus();
        return;
    }
    const create = row.hasAttribute(CREATE);
    const stays = has(root, "multiple") && !create;
    if (stays) {
        // Still the pointer's row when the pointer pressed it: it goes dark when the pointer leaves.
        light(root, row, pointed);
    } else if (!create && input && has(root, "strict")) {
        const says = row.getAttribute("data-rask-list-says");
        state.said = says === null ? labelOf(row) : says;
        state.over = input.getAttribute("value");
        state.anew = true;
    }
    // After this click has reached the page's own handler, not before: what closing empties, a create row's
    // handler still has to be sent.
    afterClick(function () {
        if (!stays) {
            setShown(popup, false);
            return;
        }
        if (input && input.value !== "" && !has(root, "stay")) {
            unsearch(root, input);
        }
    });
}

// A choice among several — a row switched, a pill taken off — empties what was typed and leaves the list open.
function unsearch(root: Element, input: HTMLInputElement): void {
    const state = stateOf(root);
    rest(root, input, "");
    state.typing = false;
    if (has(root, "top")) {
        state.top = true;
        state.cursor = null;
    }
    refilter(root);
}

// What follows a press once the page has heard it: run when the click reaches the window, past every listener
// on the document — and by a timer as well, for a click that was handed over late and never travels there.
let pending: (() => void)[] = [];

function settlePress(): void {
    const runs = pending;
    pending = [];
    for (const run of runs) {
        run();
    }
}

function afterClick(run: () => void): void {
    pending.push(run);
    setTimeout(settlePress, 0);
}

function disabled(el: Element): boolean {
    return el.matches(":disabled,[data-disabled],[aria-disabled=true]") || (el as HTMLInputElement).readOnly === true;
}

if (page) {
    const doc = page;
    if (typeof window !== "undefined") {
        window.addEventListener("click", settlePress);
    }

    // `toggle` and `beforetoggle` do not bubble; they are caught on the way down. Before, so that what the
    // list says about itself is there in the task it opens — as Flux UI writes it.
    listen("beforetoggle", function (e) {
        const popup = e.target;
        const root = popup instanceof Element ? popup.closest(ROOT) : null;
        if (!root || popupOf(root) !== popup) {
            return;
        }
        if ((e as ToggleEvent).newState === "open") {
            opened(root, popup as Element);
        } else {
            closed(root, popup as Element);
        }
    }, true);

    // Once it shows: the lit row is brought into view, and a text input the list hangs from has the focus —
    // its chevron, or the trigger around it, opened the list.
    listen("toggle", function (e) {
        const popup = e.target;
        const root = popup instanceof Element ? popup.closest(ROOT) : null;
        if (!root || popupOf(root) !== popup || !isShown(popup as Element)) {
            return;
        }
        const state = stateOf(root);
        const input = inputOf(root);
        const active = doc.activeElement;
        if (input && !(popup as Element).contains(input) && active !== input && (!active || active === doc.body || root.contains(active))) {
            input.focus();
        }
        if (state.cursor) {
            reveal(state.cursor);
        }
    }, true);

    // The mouse's events, not the pointer's: a row that slides under a pointer at rest is told to the page as a
    // mouseover (the browser catching up after a layout), and as no pointer event at all.
    listen("mousemove", function (e) {
        pointerX = e.clientX;
        pointerY = e.clientY;
    }, {capture: true, passive: true});

    listen("mouseover", function (e) {
        const row = near(e.target, OPTION);
        const root = row ? row.closest(ROOT) : null;
        const from = e.relatedTarget;
        if (!row || !root || isOff(row) || (from instanceof Node && row.contains(from))) {
            return;
        }
        const state = stateOf(root);
        // Entered by a pointer that moved. One at rest that a row slid under has entered nothing — until it
        // has picked in a list that stays open, where Flux's cursor follows the rows a new pill pushes down.
        if (state.acts || e.clientX !== pointerX || e.clientY !== pointerY) {
            light(root, row, true);
        }
    }, {capture: true, passive: true});

    listen("mouseout", function (e) {
        const list = near(e.target, "[role=listbox]");
        const root = list ? list.closest(ROOT) : null;
        const to = e.relatedTarget;
        if (!list || !root || (to instanceof Node && list.contains(to))) {
            return;
        }
        // What the pointer lit goes dark with it; the row the keys are on stays.
        const state = stateOf(root);
        if (state.pointed) {
            light(root, null);
            state.none = true;
        }
    }, {capture: true, passive: true});

    // A press on a row of a list whose answer is the input's text leaves the focus in the input, as Flux's does:
    // a blur would hand the page what was typed, before the pick says what it is now.
    listen("mousedown", function (e) {
        const root = near(e.target, ROOT);
        const popup = root ? popupOf(root) : null;
        if (root && popup && has(root, "text") && e.target instanceof Node && popup.contains(e.target)) {
            e.preventDefault();
        }
    }, true);

    listen("pointerdown", function (e) {
        const root = near(e.target, ROOT);
        const popup = root ? popupOf(root) : null;
        if (!root || !popup) {
            return;
        }
        const state = stateOf(root);
        state.shown = isShown(popup);
        state.pressed = !(e.target instanceof Node && popup.contains(e.target));
    }, {capture: true, passive: true});

    listen("click", function (e) {
        const target = e.target;
        const root = near(target, ROOT);
        const popup = root ? popupOf(root) : null;
        if (!root || !popup || !(target instanceof Element)) {
            return;
        }
        const state = stateOf(root);
        // No press came before a click the keyboard made.
        const shown = (e as MouseEvent).detail === 0 ? isShown(popup) : state.shown;
        state.pressed = false;
        if (popup.contains(target)) {
            const row = near(target, OPTION);
            if (row && !isOff(row)) {
                const pointed = (e as MouseEvent).detail !== 0;
                state.acts = pointed && has(root, "multiple");
                picked(root, row, pointed);
            }
            return;
        }
        const trigger = near(target, TRIGGER);
        // A button that names the popover toggles it by itself.
        if (!trigger || trigger.hasAttribute("popovertarget") || disabled(trigger)) {
            return;
        }
        // Something on the trigger with a press of its own: a pill's remove, the clear button.
        const inner = near(target, "[data-rask-on-click],[data-rask-list-remove]");
        const input = inputOf(root);
        if (inner && inner !== trigger && trigger.contains(inner)) {
            if (input && input.value !== "" && inner.hasAttribute("data-rask-list-remove") && !has(root, "stay")) {
                // Flux empties what was typed when a pill is taken off, as at a pick. Once the page has heard the press.
                afterClick(function () { unsearch(root, input); });
            }
            return;
        }
        if (input && !popup.contains(input)) {
            // An input's list opens on every press, and reopens as it was when that press had shut it.
            show(root);
            if (doc.activeElement !== input) {
                input.focus();
            }
            if (trigger === input && has(root, "strict") && !state.typing && input.value !== "") {
                input.select();
            }
        } else if (!shown) {
            show(root);
        }
    });

    listen("keydown", function (e) {
        const t = e.target;
        if (e.ctrlKey || e.altKey || e.metaKey || e.isComposing || !(t instanceof HTMLElement)) {
            return;
        }
        const root = near(t, ROOT);
        const popup = root ? popupOf(root) : null;
        if (!root || !popup) {
            return;
        }
        const input = t instanceof HTMLInputElement && t.getAttribute("role") === "combobox" ? t : null;
        const trigger = input || near(t, TRIGGER);
        // Focus on the list itself, or on something of the page's own inside it: the keys are not the list's.
        if (!trigger || (!input && popup.contains(t)) || disabled(trigger)) {
            return;
        }
        const state = stateOf(root);
        const key = e.key;
        const shown = isShown(popup);
        const letters = !input && key.length === 1 && key !== " " && !popup.querySelector("input");

        if (input && key === "Backspace" && input.value === "") {
            // In an empty input among the pills, Backspace takes the last pill off.
            const pills = root.querySelectorAll<HTMLElement>("[data-rask-list-remove]");
            if (pills.length) {
                pills[pills.length - 1].click();
            }
            return;
        }
        if (input && (key === "Escape" || key === "Tab") && !popup.contains(input)) {
            if (has(root, "text") && key === "Escape") {
                // Escape closes the list and empties the input, open or not. The page is told when it held
                // something (the `value` it rendered); what was typed and never handed over just goes.
                state.typing = false;
                const held = (input.getAttribute("value") || "") !== "";
                say(input, "", held ? "change" : "");
                if (!held && seam.hear) {
                    seam.hear(input, false);
                }
            } else if (has(root, "clears")) {
                // Emptied at the key, whatever the list is doing: a closed pillbox keeps nothing typed.
                state.typing = false;
                rest(root, input, "");
            } else if (has(root, "strict") && !shown) {
                // Escape on a closed combobox takes the words and leaves the answer; Tab leaves with the answer.
                state.typing = key === "Escape";
                rest(root, input, key === "Escape" ? "" : answer(state, input), key === "Escape");
            }
        }
        if (shown) {
            if (key === "ArrowDown" || key === "ArrowUp") {
                step(root, key === "ArrowDown" ? 1 : -1);
            } else if (key === "Enter") {
                press(state.cursor);
            } else if (key === "Tab") {
                setShown(popup, false);
            } else if (letters) {
                const hit = ahead(root, key);
                if (hit) {
                    light(root, hit);
                }
            }
            if (CONTAIN.indexOf(key) >= 0) {
                e.preventDefault();
            }
            return;
        }
        if (key === "ArrowDown" || key === "ArrowUp" || (key === " " && !input && !(trigger instanceof HTMLButtonElement))) {
            // A <button> is pressed by Space by itself; any other trigger opens on it here.
            e.preventDefault();
            show(root);
        } else if (letters) {
            // On a shut list a letter picks, as a native select's does.
            press(ahead(root, key));
        }
    }, true);

    listen("input", function (e) {
        const input = e.target;
        if (saying || !(input instanceof HTMLInputElement)) {
            return;
        }
        const root = input.closest(ROOT);
        const popup = root ? popupOf(root) : null;
        if (!root || !popup || inputOf(root) !== input) {
            return;
        }
        const state = stateOf(root);
        note(state, input, true);
        if (!popup.contains(input)) {
            state.typing = true;
            show(root);
        }
        state.top = true;
        state.none = false;
        state.cursor = null;
        refilter(root);
    }, true);

    // A strict input left while its list is shut says its answer again.
    listen("focusout", function (e) {
        const input = e.target;
        const root = input instanceof HTMLInputElement ? input.closest(ROOT) : null;
        if (!root || !has(root, "strict") || inputOf(root) !== input || open.has(root)) {
            return;
        }
        const state = stateOf(root);
        if (!state.resumes) {
            state.typing = false;
            rest(root, input as HTMLInputElement, answer(state, input as HTMLInputElement));
        }
    }, true);
}
