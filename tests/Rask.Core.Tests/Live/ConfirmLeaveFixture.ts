// Node-driven fixture for rask-leave.ts — when a form that asks before it is left (data-rask-confirm-leave)
// counts as unsaved.
//
// The guard listens on the document as it is imported, so the page it finds is made first: a document that
// keeps its listeners and its forms, a window that says what listens for `beforeunload`, and an observer the
// fixture fires by hand, as the browser would after a render changed an attribute. The C# test
// (ConfirmLeaveClientTests) runs this in a node subprocess and asserts the JSON line.

type Heard = (e: Raised) => void;

// An event as the guard reads one: what it was raised on, and whether a listener refused it.
interface Raised {
    target: unknown;
    key?: string;
    cancelable: boolean;
    defaultPrevented: boolean;
    preventDefault(): void;
    stopImmediatePropagation(): void;
}

const heard: Record<string, Heard[]> = {};
const onWindow = new Set<string>();
let observed: (() => void) | null = null;

// An element as the guard reads one: its attributes, and the elements it is inside.
class Stub {
    // A dialog's: whether it is showing, and how many times something closed it.
    open = false;
    closed = 0;

    constructor(readonly tag: string, readonly attributes: Record<string, string>, readonly parent: Stub | null = null) {
    }

    // The guarded forms inside this one.
    querySelectorAll(selector: string): Stub[] {
        return forms.filter((form) => form !== this && form.matches(selector) && form.inside(this));
    }

    querySelector(): Stub | null { return null; }

    inside(other: Stub): boolean { return this.parent === other || (!!this.parent && this.parent.inside(other)); }

    dispatchEvent(): boolean { return true; }

    close(): void { this.open = false; this.closed++; }

    hasAttribute(name: string): boolean { return name in this.attributes; }

    getAttribute(name: string): string | null { return name in this.attributes ? this.attributes[name] : null; }

    // `form[a]` or `[a]`: the nearest element, this one included, of that tag (when one is named) that
    // carries every attribute named.
    matches(selector: string): boolean {
        const tag = selector.match(/^\w+/);
        const named = selector.match(/[\w-]+(?=])/g) || [];
        return (!tag || tag[0] === this.tag) && named.every((name) => name in this.attributes);
    }

    closest(selector: string): Stub | null {
        return this.matches(selector) ? this : this.parent ? this.parent.closest(selector) : null;
    }
}

const forms: Stub[] = [];

const page = globalThis as unknown as Record<string, unknown>;
page.window = globalThis;
page.Element = Stub;
page.location = {href: "https://app.test/edit"};
page.addEventListener = function (type: string) { onWindow.add(type); };
page.removeEventListener = function (type: string) { onWindow.delete(type); };
let answer = false;
let asked = 0;
page.confirm = function () { asked++; return answer; };
page.MutationObserver = class {
    constructor(changed: () => void) { observed = changed; }
    observe(): void { /* fired by the fixture */ }
};
page.document = {
    documentElement: new Stub("html", {}),
    addEventListener(type: string, listener: Heard) { (heard[type] ||= []).push(listener); },
    querySelectorAll(selector: string) { return forms.filter((form) => form.matches(selector)); },
    querySelector(selector: string) { return forms.find((form) => form.matches(selector)) || null; },
};

const {unsavedMessage} = await import("../../../src/Rask.Core/Resources/rask-leave.js");

const doc = page.document as Document;

// Raised on `target`, as every listener on the document hears it; true when one of them refused it.
function raise(type: string, target: Stub, key?: string): boolean {
    let stopped = false;
    const event: Raised = {
        target, key, cancelable: true, defaultPrevented: false,
        preventDefault() { this.defaultPrevented = true; },
        stopImmediatePropagation() { stopped = true; },
    };
    for (const listener of heard[type] || []) {
        if (stopped) break;
        listener(event);
    }
    return event.defaultPrevented;
}

function guarded(message: string, inside: Stub | null = null): Stub {
    const form = new Stub("form", {"data-rask-confirm-leave": message}, inside);
    forms.push(form);
    return form;
}

// The page drew the form again: an attribute came or went, and the browser tells whoever watches.
function render(form: Stub, name: string, value: string | null): void {
    if (value === null) delete form.attributes[name]; else form.attributes[name] = value;
    if (observed) observed();
}

// Nothing typed, nothing chosen: nothing to lose.
let form = guarded("Leave the tank?");
const untouched = unsavedMessage(doc);

// A list the page draws: the pick raised no `input`, and the page says so on the form.
render(form, "data-rask-edited", "");
const afterAPick = unsavedMessage(doc);
const closingTheTabAsks = onWindow.has("beforeunload");

// The submit the page accepted carried the pick: the mark goes with the render that says saved.
raise("submit", form);
render(form, "data-rask-saved", "1");
render(form, "data-rask-edited", null);
const afterTheSave = unsavedMessage(doc);

// Typed into as well, and saved: both are saved. A pick after that is unsaved again.
forms.length = 0;
form = guarded("Leave the tank?");
const name = new Stub("input", {}, form);
raise("input", name);
const afterTyping = unsavedMessage(doc);
render(form, "data-rask-edited", "");
raise("submit", form);
render(form, "data-rask-saved", "1");
render(form, "data-rask-edited", null);
const typedAndPickedThenSaved = unsavedMessage(doc);
render(form, "data-rask-edited", "");
const pickedAgain = unsavedMessage(doc);

// A submit the page refused changes nothing on the form: the pick is still unsaved.
forms.length = 0;
form = guarded("Leave the tank?");
render(form, "data-rask-edited", "");
raise("submit", form);
const afterARefusedSubmit = unsavedMessage(doc);

// A list's search field and a calendar's month are in the form and are none of its data: they say so, on the
// field or around it, and what is typed or chosen there loses nothing. The field beside them still counts.
forms.length = 0;
form = guarded("Leave the tank?");
const search = new Stub("input", {"data-rask-unguarded": ""}, new Stub("div", {}, form));
const month = new Stub("select", {}, new Stub("div", {"data-rask-unguarded": ""}, form));
raise("input", search);
raise("change", month);
const afterSearching = unsavedMessage(doc);
const {unasked} = await import("../../../src/Rask.Core/Resources/rask-owned.js");
unasked(function () { raise("input", new Stub("input", {}, form)); });
const afterTheRuntimeWroteAField = unsavedMessage(doc);
raise("input", new Stub("input", {}, form));
const afterTypingBesideTheSearch = unsavedMessage(doc);

// A form that does not ask is not asked about, whatever the page says of it.
forms.length = 0;
forms.push(new Stub("form", {"data-rask-edited": ""}));
const aFormThatDoesNotAsk = unsavedMessage(doc);

// A form in a modal: closing the modal is leaving it. Untouched, Escape closes it unasked.
forms.length = 0;
const dialog = new Stub("dialog", {});
dialog.open = true;
form = guarded("Leave the tank?", dialog);
const escapeUntouchedRefused = raise("keydown", new Stub("input", {}, form), "Escape");

// Typed into: Escape and a press outside ask, and the reader who stays keeps the dialog and the edits.
raise("input", new Stub("input", {}, form));
const inTheDialog = unsavedMessage(dialog as unknown as Element);
const escapeRefused = raise("keydown", new Stub("input", {}, form), "Escape");
const pressOutsideRefused = raise("cancel", dialog);
const askedWhileStaying = asked;
const stillOpen = dialog.open;

// The reader lets it go: it closes, and nothing it held is asked about again.
answer = true;
raise("cancel", dialog);
const closedOnceLetGo = dialog.closed;
dialog.open = true;
const afterLettingGo = unsavedMessage(doc);

// Typed into again, then closed by the page (a render, not the reader): a form in a closed dialog holds nothing.
raise("input", new Stub("input", {}, form));
dialog.open = false;
const inAClosedDialog = unsavedMessage(doc);

process.stdout.write(JSON.stringify({
    escapeUntouchedRefused,
    inTheDialog,
    escapeRefused,
    pressOutsideRefused,
    askedWhileStaying,
    stillOpen,
    closedOnceLetGo,
    afterLettingGo,
    inAClosedDialog,
    untouched,
    afterAPick,
    closingTheTabAsks,
    afterTheSave,
    afterTyping,
    typedAndPickedThenSaved,
    pickedAgain,
    afterARefusedSubmit,
    aFormThatDoesNotAsk,
    afterSearching,
    afterTheRuntimeWroteAField,
    afterTypingBesideTheSearch,
}) + "\n");
