// A file the upload would refuse is refused in the browser, before any of it is sent.
//
// A file input says what it takes with its own `accept` (".pdf,image/*") and `data-rask-max-size` (bytes). The
// moment the reader chooses or drops files, each is checked: one too large, or of a kind `accept` does not name,
// is taken out of the choice — the rest go on as if they alone had been chosen, and a choice with none left
// sends nothing. Then the input's sentence for it (`data-rask-too-large`, `data-rask-not-accepted`: `{0}` is the
// file's name) is written into the element `data-rask-refusal` names — as a line of the runtime's own
// (`data-rask-managed`), which a render of the page leaves where it is — and the input says `aria-invalid` and the
// element around it that takes the files (`data-rask-dropzone`) `data-invalid`, until a choice passes.
//
// Ahead of the hosts: this listens on the way DOWN, and they on the way up, so neither sees a refused file.
//
// WHAT `accept` MEANS, as the picker reads it: a token that starts with a dot is a file name's ending, any case; a
// `type/*` is every subtype of that type; anything else is one exact type. A file whose type the browser does not
// know matches only an ending.
//
// Like every check in the browser, it is a courtesy: the server still has its limits (RaskUploadOptions) and the
// handler still reads the browser's word for a name and a type.

import {disown, own, page} from "./rask-owned.js";

const MAX_SIZE = "data-rask-max-size";
const TOO_LARGE = "data-rask-too-large";
const NOT_ACCEPTED = "data-rask-not-accepted";
const REFUSAL = "data-rask-refusal";
const DROPZONE = "[data-rask-dropzone]";
const MANAGED = "data-rask-managed";

/** The name, size and type of a file, as far as the check reads one. */
export interface Picked {
    name: string;
    size: number;
    type: string;
}

/** Whether `accept` (the attribute's value) takes `file`. An empty list takes anything. */
export function accepts(accept: string | null, file: Picked): boolean {
    const tokens = (accept || "").split(",").map(function (t) { return t.trim().toLowerCase(); }).filter(Boolean);
    if (tokens.length === 0) return true;
    const name = file.name.toLowerCase();
    const type = (file.type || "").toLowerCase();
    for (const token of tokens) {
        if (token.charAt(0) === ".") {
            if (name.length > token.length && name.slice(-token.length) === token) return true;
        } else if (token.slice(-2) === "/*") {
            if (type !== "" && type.indexOf(token.slice(0, -1)) === 0) return true;
        } else if (type !== "" && type === token) {
            return true;
        }
    }
    return false;
}

/** What the input says of `file`, or null when it takes it. */
export function refusalOf(input: Element, file: Picked): string | null {
    const max = Number(input.getAttribute(MAX_SIZE));
    if (input.hasAttribute(MAX_SIZE) && max >= 0 && file.size > max) {
        return say(input.getAttribute(TOO_LARGE), file.name);
    }
    if (!accepts(input.getAttribute("accept"), file)) {
        return say(input.getAttribute(NOT_ACCEPTED), file.name);
    }
    return null;
}

function say(sentence: string | null, name: string): string {
    return (sentence || "{0}").split("{0}").join(name);
}

function markInvalid(input: Element, invalid: boolean): void {
    const zone = input.closest(DROPZONE);
    for (const [el, name, value] of [[input, "aria-invalid", "true"], [zone, "data-invalid", ""]] as [Element | null, string, string][]) {
        if (!el) continue;
        if (invalid) {
            own(el, name);
            el.setAttribute(name, value);
        } else if (el.hasAttribute(name)) {
            el.removeAttribute(name);
            disown(el, name);
        }
    }
}

/** What the element `input` names says now: the runtime's line in it, or "". */
export function saying(input: Element): string {
    const id = input.getAttribute(REFUSAL);
    const said = id ? input.ownerDocument.getElementById(id) : null;
    const line = said ? said.querySelector("[" + MANAGED + "]") : null;
    return line ? line.textContent || "" : "";
}

/** Says `sentences` where the input asks, or takes what was said away when there are none. */
function tell(input: Element, sentences: string[]): void {
    const id = input.getAttribute(REFUSAL);
    const said = id ? input.ownerDocument.getElementById(id) : null;
    if (said) {
        let line = said.querySelector("[" + MANAGED + "]");
        if (sentences.length === 0) {
            if (line) line.remove();
        } else {
            if (!line) {
                line = input.ownerDocument.createElement("span");
                line.setAttribute(MANAGED, "");
                said.appendChild(line);
            }
            line.textContent = sentences.join(" ");
        }
    }
    markInvalid(input, sentences.length > 0);
}

/** A list of files as `input.files` takes one, where the browser can build one. */
function listOf(files: File[]): FileList | null {
    if (typeof DataTransfer !== "function") return null;
    try {
        const transfer = new DataTransfer();
        for (const file of files) transfer.items.add(file);
        return transfer.files;
    } catch (e) {
        return null; // an engine that builds no DataTransfer by hand
    }
}

/** Checks what was just chosen in `input`; takes the refused files out and says why. */
export function check(input: HTMLInputElement): void {
    if (!input.hasAttribute(MAX_SIZE) && !input.hasAttribute("accept") && !input.hasAttribute(REFUSAL)) return;
    const chosen = input.files ? Array.from(input.files) : [];
    const kept: File[] = [];
    const sentences: string[] = [];
    for (const file of chosen) {
        const refused = refusalOf(input, file);
        if (refused === null) kept.push(file); else sentences.push(refused);
    }
    if (kept.length !== chosen.length) {
        const list = kept.length > 0 ? listOf(kept) : null;
        if (list) input.files = list; else input.value = ""; // none left, or no way to keep some: send nothing
    }
    tell(input, sentences);
}

if (page) {
    page.addEventListener("change", function (e) {
        const input = e.target as HTMLInputElement | null;
        if (input && input.tagName === "INPUT" && input.type === "file") check(input);
    }, true);
}
