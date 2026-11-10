// Script fixture for the runtime's form and upload behaviour that needs no round trip: the `data-dirty` marks
// (rask-dirty.ts), the checks of a chosen file (rask-file-checks.ts), a chosen image's preview (rask-preview.ts) and
// calling an upload off (rask-upload.ts, rask-upload-cancel.ts).
//
// The modules are driven through what they export, against a stub DOM made here: elements that keep their
// attributes, their parent and the few properties a field has. The C# test (DirtyAndUploadClientTests) runs this on
// Jint and asserts what it reports.

import {report} from "../../Rask.TestSupport/ScriptFixtures/fixture.js";

class Stub {
    attributes: Record<string, string> = {};
    children: Stub[] = [];
    parent: Stub | null = null;
    isConnected = true;
    // A field's.
    value = "";
    defaultValue = "";
    checked = false;
    defaultChecked = false;
    selected = false;
    defaultSelected = false;
    multiple = false;
    name = "";
    files: {length: number; [i: number]: unknown} | null = null;
    options: Stub[] = [];
    textContent = "";
    style = {set: {} as Record<string, string>, setProperty(n: string, v: string) { this.set[n] = v; }, removeProperty(n: string) { delete this.set[n]; }};

    constructor(readonly tagName: string, attributes: Record<string, string> = {}, parent: Stub | null = null) {
        this.attributes = attributes;
        if (parent) {
            this.parent = parent;
            parent.children.push(this);
        }
        if ("name" in attributes) this.name = attributes.name;
    }

    get type(): string { return this.attributes.type || "text"; }

    get ownerDocument(): unknown { return doc; }

    hasAttribute(n: string): boolean { return n in this.attributes; }

    getAttribute(n: string): string | null { return n in this.attributes ? this.attributes[n] : null; }

    setAttribute(n: string, v: string): void { this.attributes[n] = String(v); }

    removeAttribute(n: string): void { delete this.attributes[n]; }

    matches(selector: string): boolean { return selector.split(",").some((one) => matchOne(this, one.trim())); }

    closest(selector: string): Stub | null {
        return this.matches(selector) ? this : this.parent ? this.parent.closest(selector) : null;
    }

    all(): Stub[] { return this.children.flatMap((c) => [c, ...c.all()]); }

    querySelectorAll(selector: string): Stub[] { return this.all().filter((el) => el.matches(selector)); }

    querySelector(selector: string): Stub | null { return this.querySelectorAll(selector)[0] || null; }

    appendChild(child: Stub): Stub {
        child.parent = this;
        this.children.push(child);
        return child;
    }

    remove(): void {
        if (this.parent) this.parent.children.splice(this.parent.children.indexOf(this), 1);
        this.parent = null;
        this.isConnected = false;
    }
}

// `tag`, `[a]`, `[a=v]`, `[a="v"]` and `:not(…)` of those: what the modules ask.
function matchOne(el: Stub, selector: string): boolean {
    const not = selector.match(/:not\((.*)\)$/);
    if (not) return matchOne(el, selector.slice(0, not.index)) && !matchOne(el, not[1]);
    const tag = selector.match(/^[a-z]+/i);
    if (tag && tag[0].toUpperCase() !== el.tagName) return false;
    for (const [, name, value] of selector.matchAll(/\[([\w-]+)(?:=["']?([^"'\]]*)["']?)?]/g)) {
        if (name === "type" && value !== undefined) {
            if (el.type !== value) return false;
            continue;
        }
        if (!el.hasAttribute(name) || (value !== undefined && el.getAttribute(name) !== value)) return false;
    }
    return true;
}

const g = globalThis as unknown as Record<string, unknown>;
g.Element = Stub;
const body = new Stub("BODY");
const doc = {
    documentElement: body,
    getElementById(id: string): Stub | null { return body.all().find((el) => el.getAttribute("id") === id) || null; },
    querySelectorAll(selector: string): Stub[] { return body.querySelectorAll(selector); },
    createElement(tag: string): Stub { return new Stub(tag.toUpperCase()); },
    // The modules listen on it as they are imported; the fixture calls what they export instead.
    addEventListener(): void { /* driven by hand */ },
};
g.document = doc;

const {changed, rendered, isDirty} = await import("../../../src/Rask.Core/Resources/rask-dirty.js");
const {accepts, check, saying} = await import("../../../src/Rask.Core/Resources/rask-file-checks.js");
const {fileFor, remember, liveUrls, refresh} = await import("../../../src/Rask.Core/Resources/rask-preview.js");
const {beginUpload} = await import("../../../src/Rask.Core/Resources/rask-upload.js");
const {pressed} = await import("../../../src/Rask.Core/Resources/rask-upload-cancel.js");

const dirty = (el: Stub) => el.hasAttribute("data-dirty");

// ----- data-dirty --------------------------------------------------------------------------------------

function form(): Stub {
    return new Stub("FORM", {"data-rask-on-submit": "h1"}, body);
}

function text(owner: Stub, value: string): Stub {
    const input = new Stub("INPUT", {type: "text", value}, owner);
    input.value = input.defaultValue = value;
    return input;
}

function type(el: Stub, value: string): void {
    el.value = value;
    changed(el as unknown as EventTarget);
}

// Typed and typed back.
let f = form();
let name = text(f, "Tank");
const untouched = dirty(f);
type(name, "Barrel");
const typed = [dirty(name), dirty(f)];
type(name, "Tank");
const typedBack = [dirty(name), dirty(f)];

// A checkbox, a select, a radio group, a date, a file input.
f = form();
const box = new Stub("INPUT", {type: "checkbox"}, f);
box.checked = true;
changed(box as unknown as EventTarget);
const checkedBox = dirty(box);
box.checked = false;
changed(box as unknown as EventTarget);
const uncheckedBox = dirty(box);

const select = new Stub("SELECT", {}, f);
for (const v of ["hu", "en"]) {
    const option = new Stub("OPTION", {value: v}, select);
    option.value = v;
    select.options.push(option);
}
select.options[0].selected = select.options[0].defaultSelected = true;
select.options[0].selected = false;
select.options[1].selected = true;
changed(select as unknown as EventTarget);
const pickedAnother = dirty(select);
select.options[1].selected = false;
select.options[0].selected = true;
changed(select as unknown as EventTarget);
const pickedBack = dirty(select);

const first = new Stub("INPUT", {type: "radio", name: "size"}, f);
const second = new Stub("INPUT", {type: "radio", name: "size"}, f);
first.checked = first.defaultChecked = true;
first.checked = false;
second.checked = true;
changed(second as unknown as EventTarget);
const radioMoved = [dirty(first), dirty(second)];
second.checked = false;
first.checked = true;
changed(first as unknown as EventTarget);
const radioBack = [dirty(first), dirty(second)];

const date = new Stub("INPUT", {type: "date", value: "2026-11-10"}, f);
date.value = date.defaultValue = "2026-11-10";
type(date, "2026-11-11");
const dateChanged = dirty(date);

const file = new Stub("INPUT", {type: "file"}, f);
file.files = {length: 1, 0: {name: "a.png"}};
changed(file as unknown as EventTarget);
const fileChosen = dirty(file);
const formWithFields = dirty(f);

// A search box, and what the runtime wrote by itself, mark nothing.
f = form();
const search = new Stub("DIV", {"data-rask-unguarded": ""}, f);
const searchBox = text(search, "");
type(searchBox, "tan");
const searching = [dirty(searchBox), dirty(f)];

// A hidden input carries a drawn control's pick: its value and its rendered value are one, so the page's word decides.
f = form();
const carrier = new Stub("INPUT", {type: "hidden", value: "Diesel"}, f);
carrier.value = carrier.defaultValue = "Petrol";
changed(carrier as unknown as EventTarget);
const hiddenPick = [dirty(carrier), dirty(f)];

// A pick only the page heard marks the form; the page taking it back unmarks it.
f = form();
f.setAttribute("data-rask-edited", "");
rendered(f as unknown as Element, "data-rask-edited");
const picked = [isDirty(f as unknown as Element), dirty(f)];
f.removeAttribute("data-rask-edited");
rendered(f as unknown as Element, "data-rask-edited");
const pickTakenBack = dirty(f);

// Saved: the render writes the saved value; a field typed after the submit stays marked.
f = form();
name = text(f, "Tank");
const notes = text(f, "");
type(name, "Barrel");
type(notes, "first");
name.defaultValue = "Barrel"; // the render after the save
notes.defaultValue = "first";
notes.value = "first and more"; // typed while the save was on its way
f.setAttribute("data-rask-saved", "1");
rendered(f as unknown as Element, "data-rask-saved");
const afterTheSave = [dirty(name), dirty(notes), dirty(f)];
type(notes, "first");
const typedBackToSaved = [dirty(notes), dirty(f)];

// Refused: nothing changes on the form, every mark stays.
f = form();
name = text(f, "Tank");
type(name, "");
const refused = [dirty(name), dirty(f)];

// Put back (Reset): the render writes the original into the field.
name.value = "Tank";
name.defaultValue = "Tank";
f.setAttribute("data-rask-settled", "1");
rendered(f as unknown as Element, "data-rask-settled");
const afterTheReset = [dirty(name), dirty(f)];

// ----- the checks of a chosen file ------------------------------------------------------------------------

const png = {name: "Photo.PNG", size: 5 * 1024 * 1024, type: "image/png"};
const pdf = {name: "invoice.pdf", size: 10, type: "application/pdf"};
const unknown = {name: "notes.md", size: 10, type: ""};
const acceptance = {
    extension: accepts(".png", png),
    otherExtension: accepts(".pdf,.docx", png),
    wildcard: accepts("image/*", png),
    exactType: accepts("application/pdf", pdf),
    exactTypeNotOther: accepts("application/pdf", png),
    unknownTypeByExtension: accepts(".md", unknown),
    unknownTypeByWildcard: accepts("text/*", unknown),
    empty: accepts("", pdf),
};

class FakeTransfer {
    items = {list: [] as unknown[], add(file: unknown) { this.list.push(file); }};

    get files(): {length: number} { const list = this.items.list; return Object.assign([...list], {length: list.length}); }
}
g.DataTransfer = FakeTransfer;

function upload(attributes: Record<string, string>): {input: Stub; zone: Stub; said: Stub} {
    const zone = new Stub("LABEL", {"data-rask-dropzone": "", "data-rask-loading": ""}, body);
    const input = new Stub("INPUT", {type: "file", "data-rask-refusal": "said-" + body.all().length, ...attributes}, zone);
    const said = new Stub("DIV", {id: input.getAttribute("data-rask-refusal")!}, body);
    return {input, zone, said};
}

const checks = {
    "data-rask-max-size": String(5 * 1024 * 1024),
    "data-rask-too-large": "{0} is larger than 5 MB.",
    "data-rask-not-accepted": "{0} is not a kind of file this takes.",
    accept: "image/*",
};
const exactly = upload(checks);
exactly.input.files = Object.assign([png], {length: 1});
check(exactly.input as unknown as HTMLInputElement);
const atTheLimit = {kept: exactly.input.files!.length, said: saying(exactly.input), invalid: exactly.zone.hasAttribute("data-invalid")};

const many = upload(checks);
const big = {name: "big.png", size: 5 * 1024 * 1024 + 1, type: "image/png"};
many.input.files = Object.assign([png, big, pdf], {length: 3});
check(many.input as unknown as HTMLInputElement);
const oneOfMany = {
    kept: many.input.files!.length,
    said: saying(many.input),
    invalid: [many.zone.hasAttribute("data-invalid"), many.input.getAttribute("aria-invalid")],
};
many.input.files = Object.assign([png], {length: 1});
check(many.input as unknown as HTMLInputElement);
const passedAfter = {said: saying(many.input), invalid: many.zone.hasAttribute("data-invalid")};

const none = upload(checks);
none.input.value = "C:\\fakepath\\big.png";
none.input.files = Object.assign([big], {length: 1});
check(none.input as unknown as HTMLInputElement);
const noneLeft = {value: none.input.value, said: saying(none.input)};

const hungarian = upload({...checks, "data-rask-too-large": "A(z) {0} túl nagy: legfeljebb 5 MB lehet."});
hungarian.input.files = Object.assign([big], {length: 1});
check(hungarian.input as unknown as HTMLInputElement);
const inHungarian = saying(hungarian.input);

// ----- a chosen image's preview ------------------------------------------------------------------------

let made = 0;
const revoked: string[] = [];
(URL as unknown as Record<string, unknown>).createObjectURL = function () { return "blob:" + ++made; };
(URL as unknown as Record<string, unknown>).revokeObjectURL = function (url: string) { revoked.push(url); };

const photo = {name: "cat.png", size: 2048, type: "image/png", lastModified: 7};
const huge = {name: "huge.jpg", size: 80 * 1024 * 1024, type: "image/jpeg", lastModified: 9};
const paper = {name: "paper.pdf", size: 4096, type: "application/pdf", lastModified: 8};
remember(Object.assign([photo, huge, paper], {length: 3}) as unknown as FileList);
const found = {
    byName: (fileFor("2048:7:cat.png") as unknown as {name: string} | null)?.name ?? null,
    tidiedName: (fileFor("2048:7:cat (1).png") as unknown as {name: string} | null)?.name ?? null,
    huge: (fileFor(huge.size + ":9:huge.jpg") as unknown as {name: string} | null)?.name ?? null,
    noImage: fileFor("4096:8:paper.pdf"),
    neverChosen: fileFor("1:1:x.png"),
};

// The page draws an item for the photo and one for the paper: the photo's <img> is filled, the paper's is not.
const item = new Stub("IMG", {"data-rask-preview": "2048:7:cat.png"}, body);
const paperItem = new Stub("IMG", {"data-rask-preview": "4096:8:paper.pdf"}, body);
refresh();
const filled = {photo: item.getAttribute("src"), paper: paperItem.getAttribute("src"), live: liveUrls()};
refresh(); // drawn again with nothing new: the same URL, none made
const madeAfterARender = made;
// Drawn for another file: the first URL goes, a new one comes.
item.setAttribute("data-rask-preview", huge.size + ":9:huge.jpg");
refresh();
const redrawn = {src: item.getAttribute("src"), revoked: revoked.length, live: liveUrls()};
// Removed (the reader pressed the cross; the page went away): its URL is revoked and none is left alive.
item.isConnected = false;
body.children.splice(body.children.indexOf(item), 1);
refresh();
const removed = {revoked: revoked.length, live: liveUrls()};

// ----- calling an upload off ----------------------------------------------------------------------------

const holder = new Stub("LABEL", {"data-rask-loading": ""}, body);
const picker = new Stub("INPUT", {type: "file"}, holder);
picker.value = "C:\\fakepath\\cat.png";
const button = new Stub("BUTTON", {"data-rask-upload-cancel": ""}, holder);
const other = new Stub("LABEL", {"data-rask-loading": ""}, body);
const otherPicker = new Stub("INPUT", {type: "file"}, other);
let aborted = 0;
let otherAborted = 0;
const going = beginUpload(picker as unknown as Element);
going.stopping(() => aborted++);
const otherGoing = beginUpload(otherPicker as unknown as Element);
otherGoing.stopping(() => otherAborted++);
const loadingBefore = holder.hasAttribute("data-loading");
const calledOff = pressed(button as unknown as EventTarget);
const cancelled = {
    calledOff,
    aborted,
    otherAborted,
    flagged: going.cancelled,
    loadingBefore,
    loadingAfter: holder.hasAttribute("data-loading"),
    otherStillLoading: other.hasAttribute("data-loading"),
    emptied: picker.value === "",
    again: pressed(button as unknown as EventTarget),
};
const again = beginUpload(picker as unknown as Element);
let abortedAgain = 0;
again.stopping(() => abortedAgain++);
pressed(button as unknown as EventTarget);
const reUpload = {abortedAgain, loading: holder.hasAttribute("data-loading")};
going.end();
otherGoing.end();

report({
    untouched, typed, typedBack,
    checkedBox, uncheckedBox, pickedAnother, pickedBack, radioMoved, radioBack, dateChanged, fileChosen, formWithFields,
    searching, hiddenPick, picked, pickTakenBack,
    afterTheSave, typedBackToSaved, refused, afterTheReset,
    acceptance, atTheLimit, oneOfMany, passedAfter, noneLeft, inHungarian,
    found, filled, madeAfterARender, redrawn, removed,
    cancelled, reUpload,
});
