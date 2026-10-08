// A date or a time typed a part at a time (data-rask-segments).
//
// A group of small inputs that behaves as one field, as Flux UI's typed date and time pickers do, key for key
// (measured on their live pages): each `<input data-rask-segment="month|day|year|hour|minute|meridiem">` takes
// digits only and shows them zero-padded; a part that can take no further digit moves on to the next; the
// horizontal arrows walk between parts and stop at the ends; the vertical arrows step a part and wrap;
// Backspace empties a part, and on an empty one steps back; a pasted date is shared out.
//
// The parts are the BROWSER's, for the reason one-time-code cells are (rask-otp.ts): rendered with no value
// and no handler, in whatever order the reader's locale puts them. The group holds ONE bound field
// (rask-bound.ts) which carries the whole value once every part is there — "yyyy-mm-dd", "HH:mm" (24-hour),
// or the two joined by "T" — announced with `input` and `change`. Emptying a part afterwards does not take
// the value back, as it does not there. When the page writes the field, the parts are refilled from it.
//
// An empty part that has focus shows its placeholder as selected text, so the reader sees which part the next
// digit goes to; it is taken out again on the way out.

import {announce, bind} from "./rask-bound.js";
import {listen, page} from "./rask-owned.js";

const GROUP = "[data-rask-segments]";
const MAX: Record<string, number> = {month: 12, day: 31, hour: 23, minute: 59};

function pad(n: number, width: number = 2): string {
    let s = "" + n;
    while (s.length < width) s = "0" + s;
    return s;
}

/**
 * What a two-digit part makes of `digit`, given the digits typed since it took focus (`typed`, "" or one
 * digit): the text to show, what is now typed, and whether the part is finished. A second digit that makes
 * too large a number starts again as the first; a first digit that no second could follow finishes at once.
 */
export function segmentDigit(kind: string, typed: string, digit: string): {text: string; typed: string; done: boolean} {
    const max = MAX[kind];
    if (typed.length === 1) {
        const n = Number(typed + digit);
        // "00": a month is 01 and moves on; a day waits for another digit (as measured, both).
        if (n <= max && (n > 0 || kind !== "day")) {
            return {text: pad(kind === "month" ? Math.max(1, n) : n), typed: "", done: true};
        }
    }
    const done = Number(digit) * 10 > max;
    return {text: "0" + digit, typed: done ? "" : digit, done};
}

/** A year takes four digits, shifting in from the right; the fourth finishes it, and year 0 is year 1. */
export function yearDigit(typed: string, digit: string): {text: string; typed: string; done: boolean} {
    const now = (typed.length >= 4 ? "" : typed) + digit;
    const done = now.length === 4;
    return {text: pad(done ? Math.max(1, Number(now)) : Number(now), 4), typed: done ? "" : now, done};
}

/** A year left after one or two digits: this century up to twenty years ahead of `thisYear`, else the last. */
export function pivotYear(typed: string, thisYear: number): string {
    if (typed.length > 2) {
        return pad(Number(typed), 4);
    }
    const century = thisYear - thisYear % 100;
    const year = century + Number(typed);
    return pad(year > thisYear + 20 ? year - 100 : year, 4);
}

/** The days in `month`; February has 29 until a `year` says otherwise. */
export function daysIn(month: number, year?: number): number {
    if (month === 2) {
        return year === undefined || (year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0)) ? 29 : 28;
    }
    return month === 4 || month === 6 || month === 9 || month === 11 ? 30 : 31;
}

/** A pasted date as [year, month, day]: "2026-03-14", or three numbers in the order the parts are in. */
export function pastedDate(text: string, order: string[]): Record<string, string> | null {
    const parts = text.trim().split(/[^0-9]+/);
    if (parts.length !== 3 || parts.some(function (p) { return !p; })) {
        return null;
    }
    const kinds = parts[0].length === 4 ? ["year", "month", "day"] : order;
    const out: Record<string, string> = {};
    for (let i = 0; i < 3; i++) {
        out[kinds[i]] = pad(Number(parts[i]), kinds[i] === "year" ? 4 : 2);
    }
    return Number(out["month"]) >= 1 && Number(out["month"]) <= 12 && Number(out["day"]) >= 1 && Number(out["day"]) <= 31
        ? out : null;
}

// ----- The parts --------------------------------------------------------------------------------------

// Digits typed into a part since it took focus, and the parts showing their placeholder as text.
const typed = new WeakMap<Element, string>();
const ghost = new WeakSet<Element>();

function partsOf(group: Element): HTMLInputElement[] {
    return Array.prototype.slice.call(group.querySelectorAll("input[data-rask-segment]"));
}

function kindOf(part: Element): string {
    return part.getAttribute("data-rask-segment") || "";
}

function part(group: Element, kind: string): HTMLInputElement | null {
    return group.querySelector<HTMLInputElement>('input[data-rask-segment="' + kind + '"]');
}

function read(el: HTMLInputElement | null): string {
    return !el || ghost.has(el) ? "" : el.value;
}

function show(el: HTMLInputElement, text: string): void {
    const focused = page!.activeElement === el;
    ghost.delete(el);
    if (!text && focused) {
        ghost.add(el);
        text = el.placeholder;
    }
    if (el.value !== text) el.value = text;
    if (focused) el.select();
}

// The hour as a 24-hour number, whatever the group shows it as; -1 for none.
function hourOf(group: Element): number {
    const text = read(part(group, "hour"));
    const meridiem = part(group, "meridiem");
    if (!text) {
        return -1;
    }
    return meridiem ? Number(text) % 12 + (read(meridiem) === "PM" ? 12 : 0) : Number(text);
}

function showHour(group: Element, hour: number, keep: boolean): void {
    const el = part(group, "hour");
    const meridiem = part(group, "meridiem");
    if (!el) {
        return;
    }
    if (!meridiem) {
        show(el, pad(hour));
        return;
    }
    show(el, pad(hour % 12 || 12));
    // 13 to 23 are the afternoon and 0 the night; 1 to 11 keep what the reader chose, the morning otherwise.
    if (!keep || hour === 0 || hour >= 12 || !read(meridiem)) {
        show(meridiem, hour >= 12 ? "PM" : "AM");
    }
}

// The whole value, or null while a part is missing or half typed.
function value(group: Element): string | null {
    const year = read(part(group, "year")), month = read(part(group, "month")), day = read(part(group, "day"));
    const minute = read(part(group, "minute"));
    const hour = hourOf(group);
    const out: string[] = [];
    if (part(group, "year") || part(group, "month") || part(group, "day")) {
        if (!year || !month || !day || typed.get(part(group, "year")!) || typed.get(part(group, "month")!)
            || typed.get(part(group, "day")!)) {
            return null;
        }
        out.push(year + "-" + month + "-" + day);
    }
    if (part(group, "hour") || part(group, "minute")) {
        if (hour < 0 || !minute) {
            return null;
        }
        out.push(pad(hour) + ":" + minute);
    }
    return out.length ? out.join("T") : null;
}

// Says the value once it is whole. A day the month does not have becomes its last one first.
function commit(group: Element): void {
    const day = part(group, "day");
    if (day && value(group) !== null) {
        const most = daysIn(Number(read(part(group, "month"))), Number(read(part(group, "year"))));
        if (Number(read(day)) > most) show(day, pad(most));
    }
    const whole = value(group);
    if (whole !== null) announce(group, whole);
}

// A part is being left: a year of one to three digits is read as a year, and a typed hour as a time of day.
function settle(el: HTMLInputElement): void {
    const group = el.closest(GROUP);
    const digits = typed.get(el);
    typed.delete(el);
    if (!group || !digits) {
        return;
    }
    if (kindOf(el) === "year") {
        show(el, pivotYear(digits, new Date().getFullYear()));
    } else if (kindOf(el) === "hour") {
        showHour(group, Number(read(el)), true);
    }
    commit(group);
}

function digit(group: Element, el: HTMLInputElement, d: string): void {
    const kind = kindOf(el);
    const parts = partsOf(group);
    if (!(kind in MAX) && kind !== "year") {
        return;
    }
    const r = kind === "year" ? yearDigit(typed.get(el) || "", d) : segmentDigit(kind, typed.get(el) || "", d);
    show(el, r.text);
    typed.set(el, r.typed);
    if (r.done && kind === "hour") {
        showHour(group, Number(r.text), true);
    }
    commit(group);
    const next = parts[parts.indexOf(el) + 1];
    if (r.done && next) {
        next.focus();
    }
}

function step(group: Element, el: HTMLInputElement, by: number): void {
    const kind = kindOf(el);
    const now = read(el);
    typed.delete(el);
    if (kind === "meridiem") {
        show(el, now === "PM" ? "AM" : "PM");
    } else if (kind === "hour") {
        const hour = hourOf(group);
        showHour(group, hour < 0 ? (by > 0 ? 0 : 23) : (hour + by + 24) % 24, false);
    } else if (kind === "year") {
        show(el, pad(now ? Math.max(1, Math.min(9999, Number(now) + by)) : new Date().getFullYear(), 4));
    } else if (kind in MAX) {
        const year = read(part(group, "year"));
        const first = kind === "minute" ? 0 : 1;
        const last = kind === "day" ? daysIn(Number(read(part(group, "month"))) || 1, year ? Number(year) : undefined) : MAX[kind];
        const n = now ? Number(now) + by : (by > 0 ? first : last);
        show(el, pad(n > last ? first : n < first ? last : n));
    }
    commit(group);
}

bind(GROUP, function (group) {
    return value(group) || "";
}, function (group, rendered) {
    const date = /(\d{1,4})-(\d\d)-(\d\d)/.exec(rendered);
    const time = /(\d\d):(\d\d)/.exec(rendered);
    const fill = function (kind: string, text: string): void {
        const el = part(group, kind);
        if (el) {
            typed.delete(el);
            show(el, text);
        }
    };
    fill("year", date ? pad(Number(date[1]), 4) : "");
    fill("month", date ? date[2] : "");
    fill("day", date ? date[3] : "");
    fill("minute", time ? time[2] : "");
    fill("hour", "");
    fill("meridiem", "");
    if (time) showHour(group, Number(time[1]), false);
});

if (page) {
    const doc = page;

    const partAt = function (e: Event): HTMLInputElement | null {
        const t = e.target;
        return t instanceof HTMLInputElement && t.hasAttribute("data-rask-segment") && !t.readOnly && t.closest(GROUP)
            ? t : null;
    };

    // Backspace empties the part; on an empty one it steps back — and, in a time, empties that one too.
    const erase = function (group: Element, el: HTMLInputElement): void {
        const parts = partsOf(group);
        const before = parts[parts.indexOf(el) - 1];
        typed.delete(el);
        if (read(el)) {
            show(el, "");
        } else if (before) {
            before.focus();
            if (kindOf(el) in {hour: 1, minute: 1, meridiem: 1}) show(before, "");
        }
    };

    const key = function (group: Element, el: HTMLInputElement, k: string): void {
        const parts = partsOf(group);
        const to = parts[parts.indexOf(el) + (k === "ArrowLeft" ? -1 : 1)];
        if (k >= "0" && k <= "9" && k.length === 1) {
            digit(group, el, k);
        } else if (k === "ArrowLeft" || k === "ArrowRight") {
            if (to) to.focus();
        } else if (k === "ArrowUp" || k === "ArrowDown") {
            step(group, el, k === "ArrowUp" ? 1 : -1);
        } else if (k === "Backspace") {
            erase(group, el);
        } else if (k === " ") {
            settle(el);
        } else if (kindOf(el) === "meridiem" && /^[aApP]$/.test(k)) {
            show(el, k.toUpperCase() + "M");
            commit(group);
        }
    };

    listen("keydown", function (e) {
        const el = partAt(e);
        // Tab is the browser's, and so is anything with a modifier — a paste arrives through its own event.
        if (!el || e.key === "Tab" || e.ctrlKey || e.altKey || e.metaKey) {
            return;
        }
        e.preventDefault();
        key(el.closest(GROUP)!, el, e.key);
    }, true);

    // A keyboard that sends no key (a phone's): the character, or the deletion, about to be made.
    listen("beforeinput", function (e) {
        const el = partAt(e);
        const input = e as InputEvent;
        if (!el || input.inputType.indexOf("Paste") >= 0) {
            return;
        }
        e.preventDefault();
        if (input.inputType === "deleteContentBackward") {
            key(el.closest(GROUP)!, el, "Backspace");
        } else if (input.data && input.data.length === 1) {
            key(el.closest(GROUP)!, el, input.data);
        }
    }, true);

    listen("paste", function (e) {
        const el = partAt(e);
        const data = (e as ClipboardEvent).clipboardData;
        if (!el || !data) {
            return;
        }
        e.preventDefault();
        const group = el.closest(GROUP)!;
        const parts = partsOf(group);
        const date = pastedDate(data.getData("text"), parts.map(kindOf).filter(function (k) {
            return k === "year" || k === "month" || k === "day";
        }));
        if (!date || !part(group, "year")) {
            return;
        }
        for (const p of parts) {
            if (date[kindOf(p)]) {
                typed.delete(p);
                show(p, date[kindOf(p)]);
            }
        }
        commit(group);
        part(group, "year")!.focus();
    }, true);

    listen("focusin", function (e) {
        const el = partAt(e);
        if (el) {
            typed.delete(el);
            show(el, read(el));
        }
    }, true);

    listen("focusout", function (e) {
        const el = partAt(e);
        if (el) {
            settle(el);
            if (ghost.has(el)) {
                ghost.delete(el);
                el.value = "";
            }
        }
    }, true);

    // The press that follows the focus would put a caret where it landed and drop the selection.
    listen("pointerup", function (e) {
        const el = partAt(e);
        if (el && el === doc.activeElement) {
            el.select();
        }
    }, true);
}
