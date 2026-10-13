// Node-driven fixture for rask-leave.ts — when a form that asks before it is left (data-rask-confirm-leave)
// counts as unsaved.
//
// The guard listens on the document as it is imported, so the page it finds is made first: a document that
// keeps its listeners and its forms, a window that says what listens for `beforeunload`, and an observer the
// fixture fires by hand, as the browser would after a render changed an attribute. The C# test
// (ConfirmLeaveClientTests) runs this in a node subprocess and asserts the JSON line.

type Heard = (e: { target: unknown }) => void;

const heard: Record<string, Heard[]> = {};
const onWindow = new Set<string>();
let observed: (() => void) | null = null;

// An element as the guard reads one: its attributes, and the elements it is inside.
class Stub {
    constructor(readonly tag: string, readonly attributes: Record<string, string>, readonly parent: Stub | null = null) {
    }

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

function raise(type: string, target: Stub): void {
    (heard[type] || []).forEach(function (listener) { listener({target}); });
}

function guarded(message: string): Stub {
    const form = new Stub("form", {"data-rask-confirm-leave": message});
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

// A form that does not ask is not asked about, whatever the page says of it.
forms.length = 0;
forms.push(new Stub("form", {"data-rask-edited": ""}));
const aFormThatDoesNotAsk = unsavedMessage(doc);

process.stdout.write(JSON.stringify({
    untouched,
    afterAPick,
    closingTheTabAsks,
    afterTheSave,
    afterTyping,
    typedAndPickedThenSaved,
    pickedAgain,
    afterARefusedSubmit,
    aFormThatDoesNotAsk,
}) + "\n");
