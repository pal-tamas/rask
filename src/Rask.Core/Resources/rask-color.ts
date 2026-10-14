// A colour picker kept in the browser (data-rask-color="<format>"), as Flux UI's script keeps its own.
//
// Held in the component, the colour the popover shows, the palette's tab stop and what was being typed were a
// round trip per arrow, per step of a drag and per character, and the dialog opening and closing was one each.
// Here they are attributes and properties this module writes and holds against the next render
// (rask-owned.ts). The page hears the VALUE, through the picker's one bound field (rask-bound.ts), and only
// when Flux's element announces one: `input` while it moves, `change` once it is taken.
//
// THE MARKUP. `data-rask-color` on the picker's root, its value one of hex hexa rgb rgba hsl hsla; the bound
// field is the root's FIRST hidden input. `data-rask-color-says` holds the words it writes, as JSON:
// `open`, `openWith` ("…, {0}"), `selected` ("{0} selected") and `area` ("…{0}%, …{1}%"). A bare
// `data-rask-color-refuses` keeps text that is no colour and announces it as it is, for the page to refuse;
// without it the colour is put back, as on Flux. Each part says what it is with `data-rask-color-part`:
//   area        the saturation and brightness surface (a `data-rask-drag="x y"`) holding the `[role=slider]` thumb
//   hue-layer   the area's backdrop, painted the hue
//   hue alpha   a slider's surface (a `data-rask-drag="x"`) holding its `input[type=range]` inside the thumb,
//               whose half is the track's inset
//   fade        the alpha track, fading to the colour
//   text        the trigger's text input; popover-text the popover's
//   button      a button trigger; opener the swatch button beside the text input (named for the colour)
//   preview     a swatch showing the page's colour; value / placeholder the button trigger's two texts
//   swatches    a `[role=listbox]` of `[role=option]` buttons, each `data-value` and named by `aria-label`
//   clear       the clear button; announce the live region; select the sheet's Select; sheet the box that
//               is two pixels wide below `sm`, where the popover is Flux's sheet
//
// THE RULES, measured on Flux's live picker (2026-10-14), each announced as Flux's element announces it:
//   * the area's arrows step saturation and brightness by 2 (10 with Shift), the page keys and Home / End go
//     to the edges; a slider's arrows step by 1 (10 with Shift), its other keys are the range input's own:
//     `input` and `change` a key;
//   * a drag: `input` on the press and on every move, `change` when it lets go;
//   * typed text: nothing while it is typed, the colour (in the picker's format) when the field changes — on
//     Enter, which also leaves it, or on leaving it; empty clears; a swatch: `input` and `change`;
//   * the trigger's text input opens the popover on ArrowDown, and so does a button trigger;
//   * on the sheet everything waits for Select but a swatch, which is taken and closes it; Cancel, Escape and
//     a press outside drop what waited.

import {announce, bind, boundOf, settle} from "./rask-bound.js";
import {place} from "./rask-drag.js";
import {listen, near, own, page} from "./rask-owned.js";

const ROOT = "[data-rask-color]";
const PART = "data-rask-color-part";
const COLUMNS = 8;

/** Hue 0–360, saturation and brightness 0–100, alpha 0–1: what the area and the sliders move. */
export interface Hsv {
    h: number;
    s: number;
    v: number;
    a: number;
}

/** A colour, or none: an empty picker stands at black until the first move makes a colour. */
interface Pick {
    c: Hsv;
    empty: boolean;
}

interface State {
    kept: Pick;
    shown: Pick;
    /** What is being typed, and where. */
    typing: HTMLInputElement | null;
    /** The field Enter was pressed in, left once its change is taken. */
    leaves: HTMLInputElement | null;
    /** Text that is no colour, and the field it stays in. */
    refused: {text: string; field: HTMLInputElement} | null;
    /** A colour typed into the sheet's own field waits there, and that field says the page's colour again. */
    typedOnSheet: boolean;
    /** Something was announced that has not been settled: a drag in flight. */
    moving: boolean;
}

const BLACK: Hsv = {h: 0, s: 0, v: 0, a: 1};
const NONE: Pick = {c: BLACK, empty: true};
const states = new WeakMap<Element, State>();
const stops = new WeakMap<Element, number>();

// ----- The arithmetic, Flux's to the last bit (UiColorValue.cs is the same, for the server) --------------

function hueOf(r: number, g: number, b: number, max: number, delta: number): number {
    if (delta === 0) return 0;
    const sixths = r >= max ? (g - b) / delta % 6 : g >= max ? (b - r) / delta + 2 : (r - g) / delta + 4;
    const hue = sixths * 60;
    return hue < 0 ? hue + 360 : hue;
}

/** The colour of three channels, each 0 to 255. */
export function fromRgb(red: number, green: number, blue: number, alpha: number): Hsv {
    const r = red / 255, g = green / 255, b = blue / 255;
    const max = Math.max(r, g, b);
    const delta = max - Math.min(r, g, b);
    return {h: hueOf(r, g, b, max, delta), s: max === 0 ? 0 : delta / max * 100, v: max * 100, a: alpha};
}

function rgb(c: Hsv): [number, number, number] {
    const s = c.s / 100, v = c.v / 100;
    const chroma = v * s;
    const x = chroma * (1 - Math.abs(c.h / 60 % 2 - 1));
    const m = v - chroma;
    if (isNaN(c.h)) return [NaN, NaN, NaN];
    const sector = Math.floor(c.h / 60) % 6;
    const [r, g, b] = sector === 0 ? [chroma, x, 0] : sector === 1 ? [x, chroma, 0] : sector === 2 ? [0, chroma, x]
        : sector === 3 ? [0, x, chroma] : sector === 4 ? [x, 0, chroma] : [chroma, 0, x];
    return [(r + m) * 255, (g + m) * 255, (b + m) * 255];
}

function whole(n: number): string {
    return isNaN(n) ? "NaN" : String(Math.round(n));
}

function hex2(n: number): string {
    if (isNaN(n)) return "NaN";
    const digits = Math.round(n).toString(16);
    return digits.length < 2 ? "0" + digits : digits;
}

function hundredths(a: number): string {
    return String(Math.round(a * 100) / 100);
}

function channels(c: Hsv): string {
    return rgb(c).map(whole).join(" ");
}

/** `#rrggbb`: what a swatch is matched by. */
export function hexOf(c: Hsv): string {
    return "#" + rgb(c).map(hex2).join("");
}

/** The colour as a style paints it. */
export function cssOf(c: Hsv): string {
    return c.a >= 1 ? "rgb(" + channels(c) + ")" : "rgb(" + channels(c) + " / " + hundredths(c.a) + ")";
}

function hsl(c: Hsv): [string, string, string] {
    const [red, green, blue] = rgb(c);
    const r = Math.round(red) / 255, g = Math.round(green) / 255, b = Math.round(blue) / 255;
    const max = Math.max(r, g, b);
    const delta = max - Math.min(r, g, b);
    const lightness = (max + (max - delta)) / 2;
    const saturation = delta === 0 ? 0 : delta / (1 - Math.abs(2 * lightness - 1));
    return [whole(hueOf(r, g, b, max, delta)), whole(saturation * 100), whole(lightness * 100)];
}

/** The colour as `format` writes it. */
export function formatOf(c: Hsv, format: string): string {
    const [h, s, l] = hsl(c);
    switch (format) {
        case "hexa": return hexOf(c) + hex2(c.a * 255);
        case "rgb": return "rgb(" + channels(c) + ")";
        case "rgba": return "rgb(" + channels(c) + " / " + hundredths(c.a) + ")";
        case "hsl": return "hsl(" + h + " " + s + "% " + l + "%)";
        case "hsla": return "hsl(" + h + " " + s + "% " + l + "% / " + hundredths(c.a) + ")";
        default: return hexOf(c);
    }
}

/**
 * Reads the computed text of a colour as Flux does: its first numbers are red, green and blue, a fourth the
 * alpha; a channel that is not there is not a number.
 */
export function fromComputed(computed: string): Hsv {
    const found = computed.match(/\d+(?:\.\d+)?/g) || [];
    const at = function (i: number): number { return i < found.length ? Number(found[i]) : NaN; };
    return fromRgb(at(0), at(1), at(2), found.length > 3 ? Math.min(at(3), 1) : 1);
}

let probe: HTMLElement | null = null;

/** The colour `text` names, read through the browser as Flux reads it; null for text that is no colour. */
function parse(text: string): Hsv | null {
    if (!page || !text.trim()) return null;
    if (!probe) {
        probe = page.createElement("span");
        probe.setAttribute("data-rask-managed", "");
        probe.style.display = "none";
    }
    probe.style.color = "";
    probe.style.color = text.trim();
    if (!probe.style.color) return null;
    page.body.appendChild(probe);
    const computed = getComputedStyle(probe).color;
    probe.remove();
    return fromComputed(computed);
}

// ----- What the picker holds ------------------------------------------------------------------------------

function formatOfRoot(root: Element): string {
    return root.getAttribute("data-rask-color") || "hex";
}

function textOf(root: Element, pick: Pick): string {
    return pick.empty ? "" : formatOf(pick.c, formatOfRoot(root));
}

function pickOf(text: string): Pick {
    const c = parse(text);
    return c ? {c, empty: false} : NONE;
}

function same(a: Pick, b: Pick): boolean {
    return a.empty === b.empty && a.c.h === b.c.h && a.c.s === b.c.s && a.c.v === b.c.v && a.c.a === b.c.a;
}

function stateOf(root: Element): State {
    let state = states.get(root);
    if (!state) {
        const bound = boundOf(root);
        const kept = pickOf(bound ? bound.getAttribute("value") || "" : "");
        states.set(root, state = {kept, shown: kept, typing: null, leaves: null, refused: null, typedOnSheet: false, moving: false});
    }
    return state;
}

function says(root: Element, word: string): string {
    try {
        const all = JSON.parse(root.getAttribute("data-rask-color-says") || "{}") as {[word: string]: string};
        return all[word] || "";
    } catch (e) {
        return "";
    }
}

function parts(root: Element, name: string): HTMLElement[] {
    const found: HTMLElement[] = [];
    root.querySelectorAll<HTMLElement>("[" + PART + "]").forEach(function (el) {
        if (el.getAttribute(PART) === name && el.closest(ROOT) === root) found.push(el);
    });
    return found;
}

function dialogOf(root: Element): HTMLDialogElement | null {
    return root.querySelector("dialog");
}

function isOpen(root: Element): boolean {
    const dialog = dialogOf(root);
    return !!dialog && dialog.open;
}

// Flux asks the window how wide it is; the kit asks the stylesheet, through a box only it sizes.
function onSheet(root: Element): boolean {
    const box = parts(root, "sheet")[0];
    return !!box && box.getBoundingClientRect().width > 1.5;
}

function waits(root: Element): boolean {
    return isOpen(root) && onSheet(root);
}

// ----- Drawing --------------------------------------------------------------------------------------------

function set(el: Element, name: string, value: string | null): void {
    own(el, name);
    if (value === null) {
        el.removeAttribute(name);
    } else if (el.getAttribute(name) !== value) {
        el.setAttribute(name, value);
    }
}

function paint(el: HTMLElement, property: string, value: string): void {
    own(el, "style");
    el.style.setProperty(property, value);
}

function fraction(n: number): string {
    return "" + (isNaN(n) ? 0 : Math.round(Math.max(0, Math.min(1, n)) * 10000) / 10000);
}

function channelValue(c: Hsv, alpha: boolean): number {
    const value = alpha ? c.a * 100 : c.h;
    return isNaN(value) ? 0 : Math.round(value);
}

/** The swatches of a palette: each one's value as a colour, and its name. */
function swatchesOf(list: Element): HTMLElement[] {
    return Array.prototype.slice.call(list.querySelectorAll("[role=option]"));
}

// The swatch a colour is told from, by what the picker writes: a colour between two of an 8-bit format's steps
// is the swatch it is written as.
function matched(root: Element, pick: Pick): string {
    const written = parse(textOf(root, pick));
    return written ? hexOf(written) : hexOf(pick.c);
}

function indexIn(swatches: HTMLElement[], hex: string): number {
    for (let i = 0; i < swatches.length; i++) {
        const c = parse(swatches[i].getAttribute("data-value") || "");
        if (c && hexOf(c) === hex) return i;
    }
    return -1;
}

function said(root: Element, state: State, field: HTMLInputElement): string {
    if (state.refused && state.refused.field === field) return state.refused.text;
    const popover = field.getAttribute(PART) === "popover-text";
    return textOf(root, popover && !state.typedOnSheet ? state.shown : state.kept);
}

function draw(root: Element): void {
    const state = stateOf(root);
    const shown = state.shown.c;
    const kept = state.kept;

    for (const layer of parts(root, "hue-layer")) {
        paint(layer, "background-color", cssOf({h: isNaN(shown.h) ? 0 : shown.h, s: 100, v: 100, a: 1}));
    }
    for (const area of parts(root, "area")) {
        place(area, fraction(shown.s / 100) + " " + fraction(1 - shown.v / 100));
        const thumb = area.querySelector("[role=slider]");
        if (thumb) {
            const saturation = whole(isNaN(shown.s) ? 0 : shown.s);
            set(thumb, "aria-valuenow", saturation);
            set(thumb, "aria-valuetext", says(root, "area").replace("{0}", saturation).replace("{1}", whole(isNaN(shown.v) ? 0 : shown.v)));
        }
    }
    for (const name of ["hue", "alpha"]) {
        const alpha = name === "alpha";
        for (const slider of parts(root, name)) {
            const value = channelValue(shown, alpha);
            place(slider, fraction(value / (alpha ? 100 : 360)));
            const range = slider.querySelector<HTMLInputElement>("input[type=range]");
            if (range && range.value !== String(value)) range.value = String(value);
            // The track is half a thumb shorter at each end, and the thumb is larger on the sheet.
            const thumb = range ? range.parentElement : null;
            const half = thumb ? Math.round(thumb.getBoundingClientRect().width / 2) : 0;
            if (half > 0) set(slider, "data-rask-drag-inset", String(half));
        }
    }
    for (const fade of parts(root, "fade")) {
        paint(fade, "background", "linear-gradient(to right, transparent, " + cssOf({h: shown.h, s: shown.s, v: shown.v, a: 1}) + ")");
    }
    for (const field of parts(root, "text").concat(parts(root, "popover-text")) as HTMLInputElement[]) {
        if (state.typing !== field) field.value = said(root, state, field);
    }
    for (const preview of parts(root, "preview")) {
        paint(preview, "background-color", kept.empty ? "transparent" : cssOf(kept.c));
    }
    for (const value of parts(root, "value")) {
        set(value, "hidden", kept.empty ? "" : null);
        if (value.textContent !== textOf(root, kept)) value.textContent = textOf(root, kept);
    }
    for (const placeholder of parts(root, "placeholder")) {
        set(placeholder, "hidden", kept.empty ? null : "");
    }
    for (const clear of parts(root, "clear")) {
        set(clear, "hidden", kept.empty && !state.refused ? "" : null);
    }
    const lists = parts(root, "swatches");
    for (const opener of parts(root, "opener")) {
        let name = "";
        if (!kept.empty) {
            const all = lists.length ? swatchesOf(lists[0]) : [];
            const at = indexIn(all, matched(root, kept));
            name = at >= 0 ? all[at].getAttribute("aria-label") || "" : textOf(root, kept);
        }
        set(opener, "aria-label", kept.empty ? says(root, "open") : says(root, "openWith").replace("{0}", name));
    }
    for (const list of lists) {
        const swatches = swatchesOf(list);
        const picked = state.shown.empty ? -1 : indexIn(swatches, matched(root, state.shown));
        const roving = stops.get(list);
        const stop = roving !== undefined && roving < swatches.length ? roving : Math.max(picked, 0);
        swatches.forEach(function (swatch, i) {
            set(swatch, "data-selected", i === picked ? "" : null);
            set(swatch, "aria-selected", i === picked ? "true" : "false");
            set(swatch, "tabindex", i === stop ? "0" : "-1");
        });
    }
}

// ----- Choosing -------------------------------------------------------------------------------------------

// The page takes what the popover shows: `input`, and `change` unless it is still moving.
function keep(root: Element, settled: boolean): void {
    const state = stateOf(root);
    if (same(state.kept, state.shown) && !state.refused) {
        if (settled && state.moving) {
            state.moving = false;
            settle(root);
        }
        return;
    }
    state.kept = state.shown;
    state.refused = null;
    state.moving = !settled;
    announce(root, textOf(root, state.kept), settled);
}

// A choice made in the popover: the page's at once, but on the sheet it waits for Select.
function choose(root: Element, pick: Pick, settled: boolean): void {
    const state = stateOf(root);
    state.shown = pick;
    if (!waits(root)) keep(root, settled);
    draw(root);
}

function move(root: Element, c: Hsv, settled: boolean): void {
    const state = stateOf(root);
    state.typedOnSheet = false;
    parts(root, "swatches").forEach(function (list) { stops.delete(list); });
    choose(root, {c, empty: false}, settled);
}

function clamp(n: number, max: number): number {
    return Math.max(0, Math.min(max, n));
}

function areaKey(root: Element, e: KeyboardEvent): void {
    const c = stateOf(root).shown.c;
    const step = e.shiftKey ? 10 : 2;
    const to: [number, number] | null = e.key === "ArrowRight" ? [c.s + step, c.v] : e.key === "ArrowLeft" ? [c.s - step, c.v]
        : e.key === "ArrowUp" ? [c.s, c.v + step] : e.key === "ArrowDown" ? [c.s, c.v - step]
        : e.key === "PageUp" ? [c.s, 100] : e.key === "PageDown" ? [c.s, 0]
        : e.key === "Home" ? [0, c.v] : e.key === "End" ? [100, c.v] : null;
    if (to) {
        e.preventDefault();
        move(root, {h: c.h, s: clamp(to[0], 100), v: clamp(to[1], 100), a: c.a}, true);
    }
}

function channel(root: Element, alpha: boolean, value: number, settled: boolean): void {
    const c = stateOf(root).shown.c;
    if (value === channelValue(c, alpha)) {
        if (settled) keep(root, true);
        return;
    }
    move(root, alpha ? {h: c.h, s: c.s, v: c.v, a: value / 100} : {h: value, s: c.s, v: c.v, a: c.a}, settled);
}

function sliderKey(root: Element, slider: Element, e: KeyboardEvent): void {
    const by = e.key === "ArrowRight" || e.key === "ArrowUp" ? 1 : e.key === "ArrowLeft" || e.key === "ArrowDown" ? -1 : 0;
    if (!by) return; // Home, End and the page keys are the range input's own, heard as its `input`
    e.preventDefault();
    const alpha = slider.getAttribute(PART) === "alpha";
    const max = alpha ? 100 : 360;
    channel(root, alpha, clamp(channelValue(stateOf(root).shown.c, alpha) + by * (e.shiftKey ? 10 : 1), max), true);
}

function typed(root: Element, field: HTMLInputElement): void {
    const state = stateOf(root);
    const text = field.value;
    const popover = field.getAttribute(PART) === "popover-text";
    const c = parse(text);
    if (!text.trim() || c) {
        state.refused = null;
        const pick: Pick = c ? {c, empty: false} : NONE;
        if (popover) {
            choose(root, pick, true);
        } else {
            state.shown = pick;
            parts(root, "swatches").forEach(function (list) { stops.delete(list); });
            keep(root, true);
        }
        state.typedOnSheet = popover && waits(root);
    } else if (root.hasAttribute("data-rask-color-refuses")) {
        state.refused = {text, field};
        announce(root, text, true);
    }
    state.typing = null;
    parts(root, "swatches").forEach(function (list) { stops.delete(list); });
    draw(root);
    if (state.leaves === field) {
        state.leaves = null;
        field.blur();
    }
}

function pickSwatch(root: Element, list: Element, swatch: HTMLElement): void {
    const c = parse(swatch.getAttribute("data-value") || "");
    if (!c) return;
    const state = stateOf(root);
    const swatches = swatchesOf(list);
    // A pick leaves the tab stop where it was, as Flux's does: it follows the colour again when the list opens.
    const picked = state.shown.empty ? -1 : indexIn(swatches, matched(root, state.shown));
    const roving = stops.get(list);
    stops.set(list, roving !== undefined && roving < swatches.length ? roving : Math.max(picked, 0));
    for (const region of parts(root, "announce")) {
        region.textContent = says(root, "selected").replace("{0}", swatch.getAttribute("aria-label") || "");
    }
    state.shown = {c, empty: false};
    state.typedOnSheet = false;
    keep(root, true);
    const sheet = waits(root);
    draw(root);
    if (sheet) {
        const dialog = dialogOf(root);
        if (dialog) dialog.close();
    }
}

function swatchKey(root: Element, list: Element, swatch: HTMLElement, e: KeyboardEvent): void {
    if (e.key === "Enter" || e.key === " ") {
        e.preventDefault();
        pickSwatch(root, list, swatch);
        return;
    }
    const swatches = swatchesOf(list);
    const at = swatches.indexOf(swatch);
    const last = swatches.length - 1;
    const to = e.key === "ArrowRight" ? at + 1 : e.key === "ArrowLeft" ? at - 1 : e.key === "ArrowDown" ? at + COLUMNS
        : e.key === "ArrowUp" ? at - COLUMNS : e.key === "Home" ? 0 : e.key === "End" ? last : at;
    if (to !== at && to >= 0 && to <= last) {
        stops.set(list, to);
        draw(root);
        swatches[to].focus();
    }
}

function opened(root: Element, open: boolean): void {
    const state = stateOf(root);
    state.typedOnSheet = false;
    parts(root, "swatches").forEach(function (list) { stops.delete(list); });
    if (!open) state.shown = state.kept;
    const dialog = dialogOf(root);
    root.querySelectorAll("[aria-controls]").forEach(function (el) {
        if (dialog && el.getAttribute("aria-controls") === dialog.id) set(el, "aria-expanded", open ? "true" : "false");
    });
    draw(root);
}

function open(root: Element): void {
    const dialog = dialogOf(root);
    if (dialog && !dialog.open) {
        try {
            dialog.showModal();
        } catch (e) {
            // not connected
        }
    }
}

// ----- Listening ------------------------------------------------------------------------------------------

function rootOf(target: EventTarget | null): HTMLElement | null {
    return near(target, ROOT);
}

function partOf(target: EventTarget | null): HTMLElement | null {
    return near(target, "[" + PART + "]");
}

bind(ROOT, function (root) {
    const state = stateOf(root);
    return state.refused ? state.refused.text : textOf(root, state.kept);
}, function (root, rendered) {
    // The page wrote a colour of its own.
    const state = stateOf(root);
    state.kept = state.shown = pickOf(rendered);
    state.refused = null;
    draw(root);
});

if (page) {
    listen("keydown", function (e) {
        const root = rootOf(e.target);
        const part = partOf(e.target);
        if (!root || !part) return;
        const name = part.getAttribute(PART);
        if (name === "area") {
            if (near(e.target, "[role=slider]")) areaKey(root, e);
        } else if (name === "hue" || name === "alpha") {
            if (e.target instanceof HTMLInputElement && e.target.type === "range") sliderKey(root, part, e);
        } else if (name === "swatches") {
            const swatch = near(e.target, "[role=option]");
            if (swatch) swatchKey(root, part, swatch, e);
        } else if (name === "text" || name === "popover-text" || name === "button") {
            if (e.key === "ArrowDown" && name !== "popover-text") {
                open(root);
            } else if (e.key === "Enter" && name !== "button") {
                const field = part as HTMLInputElement;
                const state = stateOf(root);
                // The browser reports an edited field's change on Enter by itself; it is left once that is taken.
                if (state.typing === field && field.value !== said(root, state, field)) {
                    state.leaves = field;
                } else {
                    field.blur();
                }
            }
        }
    }, true);

    listen("input", function (e) {
        const t = e.target;
        const root = rootOf(t);
        const part = partOf(t);
        if (!root || !part || !(t instanceof HTMLInputElement)) return;
        const name = part.getAttribute(PART);
        if (name === "text" || name === "popover-text") {
            stateOf(root).typing = t;
        } else if (t.type === "hidden" && name === "area") {
            const at = t.value.split(" ").map(Number);
            const c = stateOf(root).shown.c;
            if (at.length === 2 && isFinite(at[0]) && isFinite(at[1])) move(root, {h: c.h, s: at[0] * 100, v: (1 - at[1]) * 100, a: c.a}, false);
        } else if (name === "hue" || name === "alpha") {
            const alpha = name === "alpha";
            const max = alpha ? 100 : 360;
            const value = t.type === "hidden" ? Math.floor(Number(t.value) * max + 0.5) : Number(t.value);
            if (isFinite(value)) channel(root, alpha, clamp(value, max), false);
        }
    }, true);

    listen("change", function (e) {
        const t = e.target;
        const root = rootOf(t);
        const part = partOf(t);
        if (!root || !part || !(t instanceof HTMLInputElement)) return;
        const name = part.getAttribute(PART);
        if (name === "text" || name === "popover-text") {
            typed(root, t);
        } else if (name === "area" || name === "hue" || name === "alpha") {
            keep(root, true);
        }
    }, true);

    listen("click", function (e) {
        const root = rootOf(e.target);
        const part = partOf(e.target);
        if (!root || !part) return;
        const name = part.getAttribute(PART);
        if (name === "swatches") {
            const swatch = near(e.target, "[role=option]");
            if (swatch) pickSwatch(root, part, swatch);
        } else if (name === "select") {
            keep(root, true);
        } else if (name === "clear") {
            const state = stateOf(root);
            state.refused = null;
            state.shown = NONE;
            keep(root, true);
            draw(root);
        }
    }, true);

    // `toggle` does not bubble; it is caught on the way down.
    listen("toggle", function (e) {
        const dialog = e.target;
        const root = dialog instanceof HTMLDialogElement ? rootOf(dialog) : null;
        if (root && dialogOf(root) === dialog) opened(root, dialog.open);
    }, true);

    page.querySelectorAll(ROOT).forEach(draw);
}
