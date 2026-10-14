// Ui.Phone's engine: intl-tel-input, the library fluxui.dev's phone field runs, and the country picker drawn
// around it. Loaded by the runtime's hook (rask-phone.ts) the first time a page carries a phone field, and by
// no page before that. The utils (Google's libphonenumber, compiled) are a file of their own, fetched when the
// first field mounts.
//
// What it does, all in the browser, as Flux's does: the number is formatted as it is typed and the field's ONE
// bound value (a hidden input) is kept equal to `getNumber()` — E.164 once the number is possible, the text as
// typed until then; the country follows a number typed with its code; the picker opens, filters, moves its
// cursor and picks with no round trip. The page hears the value, and nothing else.

import intlTelInput from "intl-tel-input";
import data from "intl-tel-input/data";

/** What the runtime's hook hands over: the two things a hook may do to a rendered element. */
export interface Kit {
    /** Writes `value` into the field's bound input and tells the page (rask-bound.ts). */
    announce(root: Element, value: string): void;
    /** Holds attribute `name` of `el` against the render (rask-owned.ts). */
    own(el: Element, name: string): void;
}

type Iti = ReturnType<typeof intlTelInput>;

interface Country {
    iso2: string;
    dialCode: string;
    name: string;
}

interface Field {
    root: HTMLElement;
    tel: HTMLInputElement;
    iti: Iti;
    kit: Kit;
    button: HTMLButtonElement;
    panel: HTMLElement;
    list: HTMLElement;
    search: HTMLInputElement;
    rows: HTMLElement[] | null;
    active: HTMLElement | null;
    ready: boolean;
}

// The utils' file name and version, written in by build.mjs: the URL changes when the bytes do.
declare const UTILS: string;

const fields = new WeakMap<Element, Field>();
const countryOf = new WeakMap<Element, Country>();

/** The page's language, which names the countries (Intl.DisplayNames, as intl-tel-input does). */
function language(): string {
    return document.documentElement.lang || navigator.language || "en";
}

function listOf(root: Element, name: string): string[] {
    const value = root.getAttribute(name);
    return value ? value.toLowerCase().split(",").filter(Boolean) : [];
}

/** Starts the field under `root`. Nothing when it already runs. */
export function mount(root: HTMLElement, kit: Kit): void {
    if (fields.has(root)) {
        return;
    }
    const tel = root.querySelector<HTMLInputElement>("input[type=tel]");
    const button = root.querySelector<HTMLButtonElement>("button[role=combobox]");
    const panel = root.querySelector<HTMLElement>("[popover]");
    const list = panel?.querySelector<HTMLElement>("[role=listbox]");
    const search = panel?.querySelector<HTMLInputElement>("input");
    if (!tel || !button || !panel || !list || !search) {
        return;
    }
    const holder = tel.parentElement;
    const only = listOf(root, "data-rask-phone-countries");
    const iti = intlTelInput(tel, {
        countrySelectorMode: "OFF",
        showFlags: false,
        separateDialCode: false,
        initialCountry: (root.getAttribute("data-rask-phone-country") || "").toLowerCase(),
        onlyCountries: only.length ? only : null,
        countryNameLocale: language(),
        strictMode: false,
        formatAsYouType: true,
        numberDisplayFormat: "NATIONAL",
        placeholderNumberPolicy: "OFF",
        loadUtils: () => import(new URL(UTILS, import.meta.url).href),
    } as Parameters<typeof intlTelInput>[1]);
    // The library wraps the input in a box of its own. The render owns every element of the field, so the input
    // goes back where it was drawn: the morph and the frame walk count the nodes the page wrote, and no other.
    const wrap = tel.parentElement;
    if (wrap && wrap !== holder) {
        wrap.replaceWith(tel);
    }
    kit.own(tel, "data-intl-tel-input-id");
    const field: Field = {root, tel, iti, kit, button, panel, list, search, rows: null, active: null, ready: false};
    fields.set(root, field);

    tel.addEventListener("input", e => {
        const detail = (e as CustomEvent).detail;
        if (detail && detail.isSetNumber) {
            return; // the page's own value, being shown
        }
        tell(field);
    });
    tel.addEventListener("countrychange", () => paint(field));

    iti.promise.then(() => {
        field.ready = true;
        const bound = root.querySelector<HTMLInputElement>("input[type=hidden]");
        if (bound && bound.value) {
            iti.setNumber(bound.value);
        }
        paint(field);
    }, () => {
        // The utils could not be fetched: the field is a plain one, and the value is what is typed.
        field.ready = true;
        paint(field);
    });
    button.closest("[data-ui-phone-country]")?.setAttribute("data-ui-phone-ready", "");
    own(field, button.closest("[data-ui-phone-country]"), "data-ui-phone-ready");
}

/** The value the field's bound input should hold now. */
export function current(root: Element): string | null {
    const field = fields.get(root);
    if (!field || !field.ready) {
        return null;
    }
    return field.iti.getNumber() || "";
}

/** The page wrote `value` into the bound input: the field shows it. */
export function adopt(root: Element, value: string): void {
    const field = fields.get(root);
    if (!field) {
        return;
    }
    field.iti.setNumber(value);
    paint(field);
}

function tell(field: Field): void {
    field.kit.announce(field.root, field.iti.getNumber() || "");
}

function own(field: Field, el: Element | null, name: string): void {
    if (el) {
        field.kit.own(el, name);
    }
}

function selected(field: Field): Country | null {
    const country = field.iti.getSelectedCountry() as Country | null;
    return country && country.iso2 ? country : null;
}

// ----- What the button shows ---------------------------------------------------------------------------

function flagSource(img: HTMLImageElement, iso2: string): string {
    return img.getAttribute("src")!.replace(/\/[A-Z]{2}\.svg/, "/" + iso2.toUpperCase() + ".svg");
}

function fillFlag(slot: Element, iso2: string): void {
    slot.setAttribute("data-country", iso2.toUpperCase());
    const img = slot.querySelector("img");
    if (img) {
        img.setAttribute("src", flagSource(img, iso2));
    }
}

function fillText(scope: Element, selector: string, text: string): void {
    const el = scope.querySelector(selector);
    if (el && el.textContent !== text) {
        el.textContent = text;
    }
}

function fill(scope: Element, country: Country): void {
    const flag = scope.querySelector("[data-ui-phone-country-flag]");
    if (flag) {
        fillFlag(flag, country.iso2);
    }
    fillText(scope, "[data-country-name]", country.name);
    fillText(scope, "[data-country-code]", "+" + country.dialCode);
    fillText(scope, "[data-country-iso]", country.iso2);
}

function paint(field: Field): void {
    const country = selected(field);
    const label = field.button.getAttribute("data-country-label") || "";
    field.button.setAttribute("aria-label", country ? label + ": " + country.name + " (+" + country.dialCode + ")" : label);
    own(field, field.button, "aria-label");
    // The button holds the picked country's copy and, for no country, Flux's globe: one of the two is shown.
    const picked = field.button.querySelector<HTMLElement>("[data-ui-phone-selected] [data-value]");
    const globe = field.button.querySelector<HTMLElement>("[data-ui-phone-selected] [data-ui-select-placeholder]");
    if (picked && globe) {
        picked.hidden = !country;
        globe.hidden = !!country;
        picked.setAttribute("data-value", country ? country.iso2 : "");
        if (country) {
            fill(picked, country);
        }
    }
    if (field.rows) {
        for (const row of field.rows) {
            mark(field, row, "data-selected", !!country && countryOf.get(row)!.iso2 === country.iso2);
            row.setAttribute("aria-selected", country && countryOf.get(row)!.iso2 === country.iso2 ? "true" : "false");
            own(field, row, "aria-selected");
        }
    }
}

// ----- The picker ---------------------------------------------------------------------------------------

function mark(field: Field, el: Element, name: string, on: boolean): void {
    if (on) {
        el.setAttribute(name, "");
    } else {
        el.removeAttribute(name);
    }
    own(field, el, name);
}

// The countries the picker lists: Flux's `countries` keeps those, `country-order` puts its own first, the rest
// alphabetically in the page's language.
function countries(field: Field): Country[] {
    const lang = language();
    let names: Intl.DisplayNames | null = null;
    try {
        names = new Intl.DisplayNames(lang, {type: "region"});
    } catch {
        names = null;
    }
    const only = listOf(field.root, "data-rask-phone-countries");
    const order = listOf(field.root, "data-rask-phone-order");
    const all = (data as Country[])
        .filter(c => !only.length || only.includes(c.iso2))
        .map(c => ({iso2: c.iso2, dialCode: c.dialCode, name: (names && names.of(c.iso2.toUpperCase())) || c.iso2}));
    const collator = new Intl.Collator(lang);
    all.sort((a, b) => collator.compare(a.name, b.name));
    const first = order.map(iso2 => all.find(c => c.iso2 === iso2)).filter((c): c is Country => !!c);
    return first.concat(all.filter(c => !order.includes(c.iso2)));
}

function build(field: Field): void {
    if (field.rows) {
        return;
    }
    const template = field.list.querySelector<HTMLTemplateElement>("template[data-ui-phone-option]");
    if (!template) {
        return;
    }
    const prototype = template.content.firstElementChild!;
    const rows: HTMLElement[] = [];
    const fragment = document.createDocumentFragment();
    for (const country of countries(field)) {
        const row = prototype.cloneNode(true) as HTMLElement;
        // Not the render's: the runtime's morph and its frame walk pass a node marked so (rask-morph.ts).
        row.setAttribute("data-rask-managed", "");
        row.id = field.list.id + "-" + country.iso2;
        row.setAttribute("value", country.iso2);
        fill(row, country);
        countryOf.set(row, country);
        rows.push(row);
        fragment.appendChild(row);
    }
    field.list.appendChild(fragment);
    field.rows = rows;
    paint(field);
}

function shown(field: Field): HTMLElement[] {
    return (field.rows || []).filter(row => !row.hasAttribute("data-hidden"));
}

function activate(field: Field, row: HTMLElement | null, scroll: boolean): void {
    if (field.active && field.active !== row) {
        field.active.removeAttribute("data-active");
    }
    field.active = row;
    if (row) {
        row.setAttribute("data-active", "");
        field.search.setAttribute("aria-activedescendant", row.id);
        if (scroll) {
            row.scrollIntoView({block: "nearest"});
        }
    } else {
        field.search.removeAttribute("aria-activedescendant");
    }
    own(field, field.search, "aria-activedescendant");
}

function filter(field: Field): void {
    const words = field.search.value.trim().toLowerCase();
    let first: HTMLElement | null = null;
    for (const row of field.rows || []) {
        const country = countryOf.get(row)!;
        const label = (country.name + " (+" + country.dialCode + ")").toLowerCase();
        const hit = !words || label.includes(words);
        mark(field, row, "data-hidden", !hit);
        if (hit && !first) {
            first = row;
        }
    }
    const empty = field.list.querySelector("[data-ui-listbox-empty]");
    if (empty) {
        mark(field, empty, "data-hidden", first !== null);
    }
    activate(field, first, false);
}

function opened(field: Field): void {
    build(field);
    field.search.value = "";
    filter(field);
    const country = selected(field);
    const row = country ? (field.rows || []).find(r => countryOf.get(r)!.iso2 === country.iso2) || null : null;
    activate(field, row || shown(field)[0] || null, false);
    for (const el of [field.button.closest("[data-ui-phone-country]"), field.button, field.panel]) {
        if (el) {
            mark(field, el, "data-open", true);
        }
    }
    field.button.setAttribute("aria-expanded", "true");
    own(field, field.button, "aria-expanded");
}

function closed(field: Field): void {
    for (const el of [field.button.closest("[data-ui-phone-country]"), field.button, field.panel]) {
        if (el) {
            mark(field, el, "data-open", false);
        }
    }
    field.button.setAttribute("aria-expanded", "false");
    field.search.value = "";
    activate(field, null, false);
}

function pick(field: Field, row: HTMLElement): void {
    const country = countryOf.get(row);
    if (!country) {
        return;
    }
    field.panel.hidePopover();
    field.tel.focus();
    field.iti.setSelectedCountry(country.iso2);
    paint(field);
}

function move(field: Field, by: number): void {
    const rows = shown(field);
    if (!rows.length) {
        return;
    }
    const at = field.active ? rows.indexOf(field.active) : -1;
    const next = at < 0 ? (by > 0 ? 0 : rows.length - 1) : Math.max(0, Math.min(rows.length - 1, at + by));
    activate(field, rows[next], true);
}

/** An event the hook heard on or inside a phone field. */
export function handle(e: Event): void {
    const target = e.target as Element | null;
    const root = target && target.closest ? target.closest("[data-rask-phone]") : null;
    const field = root ? fields.get(root) : undefined;
    if (!field || !target) {
        return;
    }
    switch (e.type) {
        case "beforetoggle":
            if (target === field.panel) {
                if ((e as ToggleEvent).newState === "open") {
                    opened(field);
                } else {
                    closed(field);
                }
            }
            break;
        case "keydown": {
            const key = (e as KeyboardEvent).key;
            if (target === field.button && (key === "ArrowDown" || key === "ArrowUp")) {
                e.preventDefault();
                field.panel.showPopover();
            } else if (target === field.search) {
                if (key === "ArrowDown" || key === "ArrowUp") {
                    e.preventDefault();
                    move(field, key === "ArrowDown" ? 1 : -1);
                } else if (key === "Enter") {
                    e.preventDefault();
                    if (field.active) {
                        pick(field, field.active);
                    }
                }
            }
            break;
        }
        case "input":
            if (target === field.search) {
                filter(field);
            }
            break;
        case "pointerover": {
            const row = target.closest<HTMLElement>("[role=option]");
            if (row && countryOf.has(row) && row !== field.active) {
                activate(field, row, false);
            }
            break;
        }
        case "click": {
            const row = target.closest<HTMLElement>("[role=option]");
            if (row && countryOf.has(row)) {
                pick(field, row);
            }
            break;
        }
    }
}
