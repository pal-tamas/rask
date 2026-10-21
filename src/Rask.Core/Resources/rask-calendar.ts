// A calendar kept in the browser (data-rask-calendar), as Flux UI's script keeps its own: the month shown, the
// day the keyboard is on, a range waiting for its end and the stretch the pointer draws to it, the date
// picker's popup and a choice held for its button.
//
// Held in the component, each was a round trip — a page of the month, an arrow, every day the pointer crossed
// while a range waited, the popup opening and closing. Here the months are drawn by this module, from what the
// page rendered (`data-rask-calendar`, JSON) and the value it holds, and the page hears the VALUE through the
// group's one bound field (rask-bound.ts), exactly when Flux's element announces one: a day picked, a range
// finished, a day of several added or taken out, a preset — and on a phone, or with a confirmation, only when
// the choice is confirmed.
//
// THE MARKUP. `data-rask-calendar` on the calendar's root, or on the date picker's around its trigger and its
// popup; its FIRST hidden input is the bound field: `yyyy-mm-dd` for a day, days joined by commas, a range as
// `start/end` and a preset's key after a third slash, nothing for none. Parts, by `data-rask-calendar-part`:
//   header    the bar with the steps and the selects, and months the grids: both the page's first paint only
//             (subtrees the page never diffs again), drawn from here on by this module
//   previous next today month year   the steps, the shortcut and the header's selects
//   narrow    a box the stylesheet makes two pixels wide on a phone, where one month is drawn
//   text      a date picker button's words; presets the list of preset ranges, each `[role=radio]` row with its
//             key as `value` and its range as `data-rask-calendar-range`; preset-select the phone's; confirm the
//             box around the button that confirms; clear the slot of the button that empties the picker
// The JSON says what to draw: the mode, today, the limits, the week, the culture's names and patterns, and the
// classes the page draws each state with.
//
// THE RULES, measured on Flux's live calendar and date picker (2026-10-15):
//   * a step, a select and the paging keys page the month and announce nothing; Home / End / PageUp / PageDown
//     leave the focus to the page; the arrows walk the days, over what cannot be picked, paging with them;
//   * a day picked announces the value (`input` and `change`); the chosen day picked again clears it; of
//     several, a day is added or taken out; a range's first day announces NOTHING and waits, the pointer and
//     the arrows drawing the stretch to the day they are on, and its last day announces the range;
//   * a pick that finishes the choice closes a date picker's popup — unless it waits for confirmation, or the
//     popup is the phone's sheet, where the choice is held until the button; Cancel, Escape and a press outside
//     drop it on a desktop and keep it on a phone (the sheet opens again with it marked, a quirk kept);
//   * the closed button opens the popup on ArrowDown and ArrowUp.

import {announce, bind, boundOf} from "./rask-bound.js";
import {isShown, listen, near, own, page, setShown} from "./rask-owned.js";
import {partsValue, refillParts} from "./rask-segments.js";

const ROOT = "[data-rask-calendar]";
const TYPED = "[data-rask-segments]";
const PART = "data-rask-calendar-part";
const DAY_MS = 86400000;

interface Look {
    cell: string; td: string; first: string; last: string; inRange: string; outside: string; unavailable: string;
    day: string; daySelected: string; dayStatic: string; dayStaticSelected: string; dayText: string; tooltipHost: string;
    todayDot: string; todayDotHidden: string; staticDot: string; staticDotHidden: string; dot: string; dotSelected: string;
    heading: string; headingHidden: string; headingText: string; folded: string; row: string; rowBelow: string;
    weekNumber: string; preset: string; presetChecked: string; placeholder: string;
}

interface Culture {
    months: string[]; monthsGenitive: string[]; monthsAbbreviated: string[]; monthsAbbreviatedGenitive: string[];
    days: string[]; daysAbbreviated: string[]; long: string; yearMonth: string; medium: string;
}

interface Config {
    mode: "single" | "multiple" | "range";
    today: string;
    min?: string; max?: string; unavailable?: string[]; minRange?: number; maxRange?: number;
    openTo?: string; forceOpenTo?: boolean;
    startDay: number; months: number; fixedWeeks?: boolean; weekNumbers?: boolean; static?: boolean;
    selectableHeader?: boolean; picker?: boolean; confirms?: boolean;
    culture: Culture; look: Look;
    placeholder?: string; previous: string; next: string; separator?: string;
}

interface Value {
    days: number[];          // a day or several, sorted; a range's start and end
    preset: string;          // a range's preset key, or ""
}

interface State {
    config: Config;
    value: Value;            // what the page holds
    pending: Value | null;   // a choice waiting for the confirming button
    view: number | null;     // the first month shown, once paged
    cursor: number | null;
    anchor: number | null;
    hover: number | null;
    /** The tab stop does not ask for the focus: a paging key let it fall. */
    dropped: boolean;
}

const states = new WeakMap<Element, State>();

// ----- Days: whole days since 1970, in UTC, so no clock change moves one --------------------------------

/** The day `yyyy-mm-dd` names, or NaN. */
export function dayOf(text: string): number {
    const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(text);
    return m ? Date.UTC(+m[1], +m[2] - 1, +m[3]) / DAY_MS : NaN;
}

/** `yyyy-mm-dd`. */
export function isoOf(day: number): string {
    const d = new Date(day * DAY_MS);
    return pad(d.getUTCFullYear(), 4) + "-" + pad(d.getUTCMonth() + 1, 2) + "-" + pad(d.getUTCDate(), 2);
}

function pad(n: number, width: number): string {
    let s = String(n);
    while (s.length < width) s = "0" + s;
    return s;
}

function parts(day: number): {y: number; m: number; d: number; w: number} {
    const date = new Date(day * DAY_MS);
    return {y: date.getUTCFullYear(), m: date.getUTCMonth(), d: date.getUTCDate(), w: date.getUTCDay()};
}

function monthOf(y: number, m: number): number {
    return Date.UTC(y, m, 1) / DAY_MS;
}

/** The first of the month `by` months on from the month `day` is in. */
export function addMonths(day: number, by: number): number {
    const p = parts(day);
    return monthOf(p.y, p.m + by);
}

function daysIn(month: number): number {
    const p = parts(month);
    return parts(monthOf(p.y, p.m + 1) - 1).d;
}

/** The rows a month is drawn as, the neighbouring months' days filling the first and last (UiCalendarGrid.Weeks). */
export function weeksOf(month: number, startDay: number, fixed: boolean): number[][] {
    const lead = (parts(month).w - startDay + 7) % 7;
    const rows = fixed ? 6 : Math.floor((lead + daysIn(month) + 6) / 7);
    const first = month - lead;
    const out: number[][] = [];
    for (let r = 0; r < rows; r++) {
        const week: number[] = [];
        for (let s = 0; s < 7; s++) week.push(first + r * 7 + s);
        out.push(week);
    }
    return out;
}

/** The ISO week a row's Thursday falls in. */
export function isoWeek(week: number[]): number {
    const thursday = week.filter(function (d) { return parts(d).w === 4; })[0];
    const year = parts(thursday).y;
    const jan4 = Date.UTC(year, 0, 4) / DAY_MS;
    const firstThursday = jan4 + ((4 - parts(jan4).w + 7) % 7) - 7 * (((4 - parts(jan4).w + 7) % 7) > 3 ? 1 : 0);
    return Math.floor((thursday - firstThursday) / 7) + 1;
}

// ----- A .NET date pattern, as the page wrote its culture's ---------------------------------------------

/** `day` written with a .NET custom pattern (d dd ddd dddd M MM MMM MMMM y yy yyyy, quotes and backslash). */
export function formatDay(day: number, pattern: string, c: Culture): string {
    const p = parts(day);
    const genitive = /(^|[^d])d{1,2}([^d]|$)/.test(pattern.replace(/'[^']*'/g, ""));
    let out = "";
    for (let i = 0; i < pattern.length;) {
        const ch = pattern[i];
        if (ch === "'" || ch === "\"") {
            const end = pattern.indexOf(ch, i + 1);
            out += pattern.slice(i + 1, end < 0 ? pattern.length : end);
            i = end < 0 ? pattern.length : end + 1;
            continue;
        }
        if (ch === "\\") {
            out += pattern[i + 1] || "";
            i += 2;
            continue;
        }
        let n = 1;
        while (pattern[i + n] === ch) n++;
        if (ch === "d") {
            out += n === 1 ? p.d : n === 2 ? pad(p.d, 2) : n === 3 ? c.daysAbbreviated[p.w] : c.days[p.w];
        } else if (ch === "M") {
            out += n === 1 ? p.m + 1 : n === 2 ? pad(p.m + 1, 2)
                : n === 3 ? (genitive ? c.monthsAbbreviatedGenitive : c.monthsAbbreviated)[p.m]
                : (genitive ? c.monthsGenitive : c.months)[p.m];
        } else if (ch === "y") {
            out += n <= 2 ? pad(p.y % 100, n) : pad(p.y, n);
        } else {
            out += pattern.slice(i, i + n);
        }
        i += n;
    }
    return out;
}

// ----- What the page holds ------------------------------------------------------------------------------

function readValue(text: string): Value {
    const t = text.trim();
    if (!t) return {days: [], preset: ""};
    if (t.indexOf("/") >= 0) {
        const [s, e, preset] = t.split("/");
        const a = dayOf(s), b = dayOf(e);
        return isNaN(a) || isNaN(b) ? {days: [], preset: ""} : {days: [a, b], preset: preset || ""};
    }
    const days = t.split(",").map(dayOf).filter(function (d) { return !isNaN(d); });
    days.sort(function (a, b) { return a - b; });
    return {days, preset: ""};
}

function writeValue(config: Config, v: Value): string {
    if (!v.days.length) return "";
    if (config.mode === "range") return isoOf(v.days[0]) + "/" + isoOf(v.days[1]) + (v.preset ? "/" + v.preset : "");
    return v.days.map(isoOf).join(",");
}

function configOf(root: Element): Config | null {
    try {
        return JSON.parse(root.getAttribute("data-rask-calendar") || "") as Config;
    } catch (e) {
        return null;
    }
}

function stateOf(root: Element): State | null {
    let state = states.get(root);
    if (!state) {
        const config = configOf(root);
        if (!config) return null;
        const bound = boundOf(root);
        const value = readValue(bound ? bound.getAttribute("value") || "" : "");
        states.set(root, state = {config, value, pending: null, view: null, cursor: null, anchor: null, hover: null, dropped: false});
    }
    return state;
}

function part(root: Element, name: string): HTMLElement | null {
    const all = root.querySelectorAll<HTMLElement>("[" + PART + "=\"" + name + "\"]");
    for (let i = 0; i < all.length; i++) {
        if (all[i].closest(ROOT) === root) return all[i];
    }
    return null;
}

function popupOf(root: Element): HTMLElement | null {
    return root.querySelector<HTMLElement>("[popover]");
}

function narrow(root: Element): boolean {
    const box = part(root, "narrow");
    return !!box && box.getBoundingClientRect().width > 1.5;
}

// A choice is held for the popup's button: where the picker confirms, and on a phone, where Flux's always does.
function holds(root: Element, state: State): boolean {
    return !!state.config.picker && (!!state.config.confirms || narrow(root));
}

function shown(state: State): Value {
    return state.pending || state.value;
}

// A typed date picker's fields, the start before the end. What is typed in them is the picker's value as soon as
// it is whole, though a bound picker sends it only with the next action: the calendar reads them, not the page.
function typedFields(root: Element): Element[] {
    return Array.prototype.filter.call(root.querySelectorAll(TYPED), function (g: Element) { return g.closest(ROOT) === root; });
}

// What the typed fields' parts say: their dates, or nothing for parts half filled or emptied; null with none.
function typedValue(root: Element, state: State): Value | null {
    const fields = typedFields(root);
    if (!fields.length) return null;
    const days = fields.map(function (g) { return dayOf(partsValue(g)); });
    const range = state.config.mode === "range";
    if (days.some(isNaN) || (range && (days.length < 2 || days[0] > days[1]))) return {days: [], preset: ""};
    const same = writeValue(state.config, {days, preset: ""}) === writeValue(state.config, {days: state.value.days, preset: ""});
    return {days: range ? days.slice(0, 2) : days.slice(0, 1), preset: same ? state.value.preset : ""};
}

// ----- What a day is ------------------------------------------------------------------------------------

function limits(config: Config): {min: number; max: number} {
    return {min: config.min ? dayOf(config.min) : -Infinity, max: config.max ? dayOf(config.max) : Infinity};
}

function unavailable(config: Config, day: number): boolean {
    return !!config.unavailable && config.unavailable.indexOf(isoOf(day)) >= 0;
}

// A waiting range with a limit rules out every day before its start and every day that makes it too short or long.
function blocks(state: State, day: number): boolean {
    const c = state.config;
    if (state.anchor === null || (c.minRange === undefined && c.maxRange === undefined) || day === state.anchor) return false;
    const count = day - state.anchor + 1;
    return day < state.anchor || count < (c.minRange || 1) || count > (c.maxRange || Infinity);
}

function blocked(state: State, day: number): boolean {
    const l = limits(state.config);
    return day < l.min || day > l.max || unavailable(state.config, day) || blocks(state, day);
}

interface Marks {
    selected: boolean; inRange: boolean; start: boolean; end: boolean; endPreview: boolean;
}

function marksOf(state: State, day: number): Marks {
    const v = shown(state);
    if (state.config.mode !== "range") {
        return {selected: v.days.indexOf(day) >= 0, inRange: false, start: false, end: false, endPreview: false};
    }
    if (state.anchor === null) {
        if (!v.days.length) return {selected: false, inRange: false, start: false, end: false, endPreview: false};
        const [s, e] = v.days;
        return {selected: day === s || day === e, inRange: day >= s && day <= e, start: day === s, end: day === e, endPreview: false};
    }
    const anchor = state.anchor;
    const hover = state.hover !== null && state.hover !== anchor ? state.hover : null;
    const ahead = hover !== null && hover > anchor;
    return {selected: day === anchor, inRange: ahead && day >= anchor && day <= hover!, start: day === anchor, end: false, endPreview: day === hover};
}

function firstChosen(state: State): number | null {
    if (state.anchor !== null) return state.anchor;
    const v = shown(state);
    return v.days.length ? v.days[0] : null;
}

function firstMonth(state: State): number {
    if (state.view !== null) return state.view;
    const c = state.config;
    const openTo = c.openTo ? dayOf(c.openTo) : null;
    const first = firstChosen(state);
    const opened = c.forceOpenTo ? (openTo !== null ? openTo : first) : (first !== null ? first : openTo);
    return addMonths(opened !== null ? opened : dayOf(c.today), 0);
}

function monthsShown(root: Element, state: State): number[] {
    const view = firstMonth(state);
    const count = narrow(root) ? 1 : Math.max(1, state.config.months);
    const out: number[] = [];
    for (let i = 0; i < count; i++) out.push(addMonths(view, i));
    return out;
}

// Where Tab lands: the keyboard's day, else the first chosen, else today, else the 1st.
function tabStop(state: State, months: number[], drawn: number[]): number {
    const wanted = [state.cursor, firstChosen(state), dayOf(state.config.today), months[0]];
    for (const d of wanted) {
        if (d !== null && drawn.indexOf(d) >= 0 && !blocked(state, d)) return d;
    }
    return months[0];
}

// ----- Drawing ------------------------------------------------------------------------------------------

function el(tag: string, cls?: string): HTMLElement {
    const e = page!.createElement(tag);
    if (cls) e.className = cls;
    return e;
}

function join(...classes: (string | false | null | undefined)[]): string {
    return classes.filter(function (c) { return !!c; }).join(" ");
}

function set(e: Element, name: string, value: string | null): void {
    if (value === null) {
        if (e.hasAttribute(name)) e.removeAttribute(name);
    } else if (e.getAttribute(name) !== value) {
        e.setAttribute(name, value);
    }
}

// One day's cell and button, for a new grid and again whenever the marks change.
function paintDay(state: State, td: HTMLElement, month: number, day: number, slot: number, stop: number | null): void {
    const c = state.config, look = c.look;
    const marks = marksOf(state, day);
    const outside = parts(day).m !== parts(month).m;
    const off = unavailable(c, day);
    const isBlocked = blocked(state, day);
    const today = day === dayOf(c.today);
    td.className = join(look.td, (slot === 0 && !c.weekNumbers) || marks.start ? look.first : "",
        slot === 6 || marks.end || marks.endPreview ? look.last : "", marks.inRange && look.inRange,
        outside && !c.static && look.outside, off && look.unavailable, look.cell);
    const data: [string, boolean][] = [["outside", outside], ["today", today], ["unavailable", off], ["selected", marks.selected],
        ["start", marks.start], ["end", marks.end], ["in-range", marks.inRange], ["end-preview", marks.endPreview]];
    for (const [name, on] of data) set(td, "data-" + name, on ? "" : null);
    set(td, "aria-disabled", !c.static && isBlocked ? "true" : null);
    set(td, "aria-selected", marks.selected || marks.inRange ? "true" : c.mode === "range" ? null : "false");
    const button = td.querySelector<HTMLButtonElement>("button");
    const dot = dotOf(td);
    if (c.static) {
        const box = td.firstElementChild as HTMLElement | null;
        if (box) box.className = join(marks.selected ? look.dayStaticSelected : look.dayStatic, look.cell);
        if (dot) dot.className = join("mb-1", marks.selected ? look.dotSelected : look.dot);
        return;
    }
    if (dot) dot.className = marks.selected ? look.dotSelected : look.dot;
    if (button) {
        button.className = join(marks.selected ? look.daySelected : look.day, look.cell);
        button.disabled = isBlocked;
        set(button, "tabindex", day === stop ? "0" : "-1");
    }
}

// The dot under today's number: in the button's text box, or in a static day's box.
function dotOf(td: Element): HTMLElement | null {
    const box = td.querySelector("button") ? td.querySelector("button")!.firstElementChild : td.firstElementChild;
    const holder = box ? box.firstElementChild : null;
    return holder ? holder.firstElementChild as HTMLElement | null : null;
}

function dayCell(state: State, month: number, day: number, slot: number, stop: number | null): HTMLElement {
    const c = state.config, look = c.look;
    const td = el("td");
    td.setAttribute("role", "gridcell");
    td.setAttribute("data-date", isoOf(day));
    const name = formatDay(day, c.culture.long, c.culture);
    const today = day === dayOf(c.today);
    const number = String(parts(day).d);
    if (c.static) {
        td.setAttribute("aria-label", name);
        const box = el("div");
        const dotBox = el("div", today ? look.staticDot : look.staticDotHidden);
        dotBox.appendChild(el("div"));
        box.appendChild(dotBox);
        box.appendChild(page!.createTextNode(number));
        td.appendChild(box);
    } else {
        const host = el("div", look.tooltipHost);
        const button = el("button") as HTMLButtonElement;
        button.type = "button";
        button.setAttribute("aria-label", name);
        const text = el("div", look.dayText);
        const dotBox = el("div", today ? look.todayDot : look.todayDotHidden);
        dotBox.appendChild(el("div"));
        const n = el("div");
        n.textContent = number;
        text.appendChild(dotBox);
        text.appendChild(n);
        button.appendChild(text);
        host.appendChild(button);
        td.appendChild(host);
    }
    paintDay(state, td, month, day, slot, stop);
    return td;
}

function monthGrid(root: Element, state: State, month: number, folded: boolean, head: Element | null, stop: number | null): HTMLElement {
    const c = state.config, look = c.look;
    const grid = el("div", folded ? look.folded : undefined);
    grid.setAttribute("role", "grid");
    grid.setAttribute("data-month", "");
    const heading = el("div", c.selectableHeader ? look.headingHidden : look.heading);
    const text = el("div", look.headingText);
    text.textContent = formatDay(month, c.culture.yearMonth, c.culture);
    heading.appendChild(text);
    grid.appendChild(heading);
    const table = el("table");
    if (head) table.appendChild(head.cloneNode(true));
    const body = el("tbody");
    weeksOf(month, c.startDay, !!c.fixedWeeks).forEach(function (week, at) {
        const tr = el("tr", at === 0 ? look.row : look.rowBelow);
        if (c.weekNumbers) {
            const wn = el("td", join("p-0 relative", look.weekNumber, look.cell));
            wn.textContent = String(isoWeek(week));
            tr.appendChild(wn);
        }
        week.forEach(function (day, slot) { tr.appendChild(dayCell(state, month, day, slot, stop)); });
        body.appendChild(tr);
    });
    table.appendChild(body);
    grid.appendChild(table);
    return grid;
}

function drawn(root: Element): HTMLElement[] {
    const grids = part(root, "months");
    return grids ? Array.prototype.slice.call(grids.querySelectorAll("[role=grid]")) : [];
}

function drawnDays(state: State, months: number[]): number[] {
    const out: number[] = [];
    for (const m of months) for (const w of weeksOf(m, state.config.startDay, !!state.config.fixedWeeks)) out.push(...w);
    return out;
}

// Two months side by side draw the days between them twice: the tab stop is the day in its own month, else
// the first month that draws it.
function hostMonth(state: State, months: number[], stop: number): number {
    for (const m of months) if (parts(m).y === parts(stop).y && parts(m).m === parts(stop).m) return m;
    for (const m of months) if (weeksOf(m, state.config.startDay, !!state.config.fixedWeeks).some(function (w) { return w.indexOf(stop) >= 0; })) return m;
    return months[0];
}

/** The months drawn again: a page, a pick, a value the page wrote. `focus` puts the focus on the tab stop. */
function draw(root: Element, focus: boolean = false): void {
    const state = stateOf(root);
    const grids = part(root, "months");
    if (!state || !grids) return;
    const months = monthsShown(root, state);
    const days = drawnDays(state, months);
    const stop = tabStop(state, months, days);
    const host = hostMonth(state, months, stop);
    const head = grids.querySelector("thead");
    const rendered = drawn(root);
    const same = rendered.length === months.length && rendered.every(function (g, i) {
        return g.querySelector("[data-date]") !== null && sameMonth(g, months[i], state);
    });
    if (same) {
        rendered.forEach(function (g, i) {
            g.querySelectorAll<HTMLElement>("td[data-date]").forEach(function (td) {
                const day = dayOf(td.getAttribute("data-date") || "");
                const slot = Array.prototype.indexOf.call(td.parentElement!.querySelectorAll("td[data-date]"), td);
                paintDay(state, td, months[i], day, slot, months[i] === host ? stop : null);
            });
        });
    } else {
        const fresh = months.map(function (m, i) { return monthGrid(root, state, m, i > 0, head, m === host ? stop : null); });
        while (grids.firstChild) grids.removeChild(grids.firstChild);
        fresh.forEach(function (g) { grids.appendChild(g); });
    }
    drawHeader(root, state, months);
    if (focus) {
        const button = grids.querySelector<HTMLElement>("button[tabindex='0']");
        if (button) button.focus();
    }
}

// The grid of `month` as it is drawn: its first day of the month says which.
function sameMonth(grid: Element, month: number, state: State): boolean {
    const lead = (parts(month).w - state.config.startDay + 7) % 7;
    const first = grid.querySelector("td[data-date]");
    return !!first && dayOf(first.getAttribute("data-date") || "") === month - lead;
}

function drawHeader(root: Element, state: State, months: number[]): void {
    const c = state.config, l = limits(c);
    const previous = part(root, "previous") as HTMLButtonElement | null;
    const next = part(root, "next") as HTMLButtonElement | null;
    if (previous) step(previous, l.min !== -Infinity && months[0] - 1 < l.min, c.previous);
    if (next) step(next, l.max !== Infinity && addMonths(months[months.length - 1], 1) > l.max, c.next);
    const p = parts(months[0]);
    const month = part(root, "month") as HTMLSelectElement | null;
    const year = part(root, "year") as HTMLSelectElement | null;
    if (month) month.value = String(p.m + 1);
    if (year) year.value = String(p.y);
}

function step(button: HTMLButtonElement, off: boolean, label: string): void {
    button.disabled = off;
    set(button, "aria-disabled", off ? "true" : null);
    set(button, "aria-label", label);
}

// The date picker's button says what is chosen: the day, the range, or the preset that named it.
function drawText(root: Element, state: State): void {
    const text = part(root, "text");
    const words = text ? text.firstElementChild as HTMLElement | null : null;
    if (!words) return;
    const c = state.config, v = state.value;
    let said = "";
    if (v.days.length) {
        if (c.mode === "range" && v.preset && v.preset !== "custom") {
            const row = presetRow(root, v.preset);
            said = row ? (row.textContent || "").trim() : "";
        } else {
            said = v.days.map(function (d) { return formatDay(d, c.culture.medium, c.culture); }).join(c.separator || " – ");
        }
    }
    if (said) {
        words.className = "";
        words.removeAttribute("class");
        words.removeAttribute("data-ui-date-picker-placeholder");
        words.textContent = said;
    } else {
        words.className = c.look.placeholder;
        words.setAttribute("data-ui-date-picker-placeholder", "");
        words.textContent = c.placeholder || "";
    }
    const clear = part(root, "clear");
    if (clear) {
        own(clear, "class");
        clear.classList.toggle("hidden", !v.days.length);
        clear.classList.toggle("flex", !!v.days.length);
    }
}

function presetRow(root: Element, key: string): HTMLElement | null {
    const list = part(root, "presets");
    return list ? list.querySelector<HTMLElement>("[role=radio][value=\"" + key + "\"]") : null;
}

// The preset that names the range is checked; the tab stop is it, or the first row while Custom is.
function drawPresets(root: Element, state: State): void {
    const list = part(root, "presets");
    const v = shown(state);
    const rows = list ? Array.prototype.slice.call(list.querySelectorAll("[role=radio]")) as HTMLElement[] : [];
    let named = "custom";
    for (const row of rows) {
        const key = row.getAttribute("value") || "";
        const range = readValue(row.getAttribute("data-rask-calendar-range") || "");
        if (key !== "custom" && v.days.length
            && (v.preset === key || (!v.preset && range.days[0] === v.days[0] && range.days[1] === v.days[1]))) {
            named = key;
            break;
        }
    }
    rows.forEach(function (row, at) {
        const key = row.getAttribute("value");
        const on = key === named;
        const stop = named === "custom" ? at === 0 : on;
        row.className = on ? state.config.look.presetChecked : state.config.look.preset;
        set(row, "aria-checked", on ? "true" : "false");
        set(row, "tabindex", stop ? "0" : "-1");
        set(row, "data-checked", on ? "" : null);
        set(row, "data-active", stop ? "" : null);
    });
    const select = part(root, "preset-select") as HTMLSelectElement | null;
    if (select) select.value = named;
}

// ----- Choosing ----------------------------------------------------------------------------------------

function commit(root: Element, state: State, value: Value): void {
    state.value = value;
    state.pending = null;
    announce(root, writeValue(state.config, value));
    drawText(root, state);
    // A typed picker's fields show the choice at once, and carry it with the page's next action.
    typedFields(root).forEach(function (g, i) {
        refillParts(g, value.days.length ? isoOf(value.days[Math.min(i, value.days.length - 1)]) : "");
    });
}

// A choice: the page's at once, or held for the button where the picker holds it. One that finishes the choice
// closes the picker's popup when nothing holds it.
function choose(root: Element, state: State, value: Value, finishes: boolean): void {
    if (holds(root, state)) {
        state.pending = value;
    } else {
        commit(root, state, value);
        if (finishes && state.config.picker) setShown(popupOf(root), false);
    }
    draw(root);
    drawPresets(root, state);
}

function pick(root: Element, state: State, day: number): void {
    if (blocked(state, day)) return;
    // The months stay where they are: a day taken out does not page back to today.
    state.view = monthsShown(root, state)[0];
    state.cursor = day;
    const v = shown(state);
    const mode = state.config.mode;
    if (mode === "single") {
        const again = v.days[0] === day;
        choose(root, state, {days: again ? [] : [day], preset: ""}, !again);
    } else if (mode === "multiple") {
        const next = v.days.filter(function (d) { return d !== day; });
        if (next.length === v.days.length) next.push(day);
        next.sort(function (a, b) { return a - b; });
        choose(root, state, {days: next, preset: ""}, false);
    } else if (state.anchor === null || day < state.anchor) {
        // A range's first day waits for its last, and the page hears nothing of it.
        state.anchor = day;
        state.hover = null;
        draw(root);
    } else {
        const start = state.anchor;
        state.anchor = state.hover = null;
        choose(root, state, {days: [start, day], preset: ""}, true);
    }
}

// The next day that way which can be picked; none when the limits end the walk first.
function nextDay(state: State, from: number, by: number): number | null {
    const l = limits(state.config);
    for (let day = from + by, tries = 0; tries < 62; day += by, tries++) {
        if (day < l.min || day > l.max) return null;
        if (!blocked(state, day)) return day;
    }
    return null;
}

function key(root: Element, state: State, e: KeyboardEvent): void {
    const by = e.key === "ArrowLeft" ? -1 : e.key === "ArrowRight" ? 1 : e.key === "ArrowUp" ? -7 : e.key === "ArrowDown" ? 7 : 0;
    const months = monthsShown(root, state);
    if (by) {
        const days = drawnDays(state, months);
        const from = tabStop(state, months, days);
        const to = nextDay(state, from, by);
        if (to === null) return;
        state.cursor = to;
        if (state.anchor !== null) state.hover = to;
        if (days.indexOf(to) < 0) {
            // Out of the drawn weeks: the view follows the day, by as little as shows it.
            state.view = to < months[0] ? addMonths(to, 0) : addMonths(to, 1 - months.length);
        }
        draw(root, true);
        return;
    }
    // Flux pages a month on all four, with or without Shift, and lets go of the focus.
    const page = e.key === "Home" || e.key === "PageUp" ? -1 : e.key === "End" || e.key === "PageDown" ? 1 : 0;
    if (page) {
        state.view = addMonths(months[0], page);
        draw(root);
    }
}

// ----- Listening ---------------------------------------------------------------------------------------

function rootOf(target: EventTarget | null): HTMLElement | null {
    return near(target, ROOT);
}

function partOf(target: EventTarget | null): HTMLElement | null {
    return near(target, "[" + PART + "]");
}

bind(ROOT, function (root) {
    const state = stateOf(root);
    return state ? writeValue(state.config, state.value) : "";
}, function (root, rendered) {
    // The page wrote a value of its own: the clear button, a typed date, its own code. Another value shows its month.
    const state = stateOf(root);
    if (!state) return;
    const value = readValue(rendered);
    if (writeValue(state.config, value) !== writeValue(state.config, state.value)) state.view = null;
    state.value = value;
    state.pending = null;
    state.anchor = state.hover = null;
    draw(root);
    drawText(root, state);
    drawPresets(root, state);
});

if (page) {
    listen("click", function (e) {
        const root = rootOf(e.target);
        const state = root ? stateOf(root) : null;
        if (!root || !state) return;
        const day = state.config.static ? null : near(e.target, "td[data-date] button");
        const at = partOf(e.target);
        const name = at ? at.getAttribute(PART) : null;
        if (day && name === "months") {
            pick(root, state, dayOf(day.closest("td")!.getAttribute("data-date") || ""));
        } else if (name === "previous" || name === "next") {
            const button = near(e.target, "button") as HTMLButtonElement | null;
            if (button && !button.disabled) {
                state.view = addMonths(monthsShown(root, state)[0], name === "next" ? 1 : -1);
                state.cursor = null;
                draw(root);
            }
        } else if (name === "today") {
            const today = dayOf(state.config.today);
            const months = monthsShown(root, state);
            if (months.some(function (m) { return parts(m).y === parts(today).y && parts(m).m === parts(today).m; })) {
                pick(root, state, today);
            } else {
                state.view = addMonths(today, 0);
                state.cursor = null;
                draw(root);
            }
        } else if (name === "presets") {
            const row = near(e.target, "[role=radio]");
            const range = row ? readValue(row.getAttribute("data-rask-calendar-range") || "") : null;
            if (row && range && range.days.length && row.getAttribute("value") !== "custom") {
                state.anchor = state.hover = null;
                state.view = null;
                choose(root, state, {days: range.days, preset: row.getAttribute("value") || ""}, true);
            }
        } else if (name === "confirm") {
            if (state.pending) commit(root, state, state.pending);
        }
    }, true);

    listen("change", function (e) {
        const root = rootOf(e.target);
        const state = root ? stateOf(root) : null;
        const at = partOf(e.target);
        if (!root || !state || !at || !(e.target instanceof HTMLSelectElement)) return;
        const name = at.getAttribute(PART);
        const p = parts(monthsShown(root, state)[0]);
        if (name === "month") {
            state.view = monthOf(p.y, Number(e.target.value) - 1);
        } else if (name === "year") {
            state.view = monthOf(Number(e.target.value), p.m);
        } else if (name === "preset-select") {
            const row = presetRow(root, e.target.value);
            const range = row ? readValue(row.getAttribute("data-rask-calendar-range") || "") : null;
            if (range && range.days.length && e.target.value !== "custom") {
                choose(root, state, {days: range.days, preset: e.target.value}, true);
            }
            return;
        } else {
            return;
        }
        state.cursor = null;
        draw(root);
    }, true);

    listen("keydown", function (e) {
        const root = rootOf(e.target);
        const state = root ? stateOf(root) : null;
        if (!root || !state || e.ctrlKey || e.altKey || e.metaKey) return;
        const at = partOf(e.target);
        if (at && at.getAttribute(PART) === "months" && near(e.target, "td[data-date] button")) {
            key(root, state, e);
        } else if ((e.key === "ArrowDown" || e.key === "ArrowUp") && near(e.target, "[popovertarget]")) {
            // The closed button opens the popup on the vertical arrows.
            const popup = popupOf(root);
            if (popup && !isShown(popup)) setShown(popup, true);
        }
    }, true);

    // The pointer draws the stretch a waiting range would end on.
    listen("mouseover", function (e) {
        const root = rootOf(e.target);
        const state = root ? stateOf(root) : null;
        const day = near(e.target, "td[data-date] button") as HTMLButtonElement | null;
        if (!root || !state || state.anchor === null || !day || day.disabled) return;
        const date = dayOf(day.closest("td")!.getAttribute("data-date") || "");
        if (state.hover !== date) {
            state.hover = date;
            draw(root);
        }
    }, true);

    // The popup opens on the month it was left on, as Flux's does — the chosen day's while nobody paged — drawn for
    // the width it opened at, measured once it shows; closing drops a choice nobody confirmed — but not on a phone,
    // whose sheet opens again with it. The chosen day is what the typed fields hold, sent or not: one typed since
    // shows its month; half filled, nothing is chosen and it opens on today.
    listen("beforetoggle", function (e) {
        const popup = e.target;
        const root = popup instanceof HTMLElement ? rootOf(popup) : null;
        const state = root ? stateOf(root) : null;
        if (!root || !state || popupOf(root) !== popup) return;
        if ((e as ToggleEvent).newState === "open") {
            const typed = typedValue(root, state);
            if (typed && writeValue(state.config, typed) !== writeValue(state.config, state.value)) {
                state.value = typed;
                state.pending = null;
                state.view = null;
            }
            state.cursor = state.anchor = state.hover = null;
            // Drawn before it shows as well, so it is placed (rask-place.ts) at the height it opens at.
            draw(root);
        } else {
            if (!narrow(root)) state.pending = null;
            draw(root);
        }
    }, true);
    listen("toggle", function (e) {
        const popup = e.target;
        const root = popup instanceof HTMLElement ? rootOf(popup) : null;
        const state = root ? stateOf(root) : null;
        if (!root || !state || popupOf(root) !== popup || (e as ToggleEvent).newState !== "open") return;
        draw(root);
        drawPresets(root, state);
    }, true);
}
