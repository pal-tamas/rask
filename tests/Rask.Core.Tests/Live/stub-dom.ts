// The typed stub DOM the Node fixtures build their scenarios on.
//
// Each fixture drives a real framework module — the morph, the diff codec — against nodes in states
// a browser only reaches through genuine user interaction: a `value` attribute and a `.value`
// property that disagree, a `.checked` flipped without its attribute, an option selected out from
// under a lagging frame. A real DOM cannot be put into those states from a test, which is why the
// stubs exist and why jsdom would not replace them.
//
// Shared because all seven fixtures hand-rolled the same linked list, and because the one dishonest
// thing here — asserting a partial fake is an Element — is worth writing once, where it can be read.
//
// Two views of the same object, which is what makes the rest of it honest:
//
//   StubNode / StubParent   what a fixture builds and mutates. The tree links are writable, unlike
//                           the browser's, because the fixture relinks by hand.
//   StubElement             what it hands to the framework: a real HTMLElement as far as the module
//                           under test is concerned, with the child list left visible so the
//                           scenarios can assert on what the morph did.

/** A node as the fixtures build it: tree links writable, because they do the linking. */
export interface StubNode {
    nodeType: number;
    nodeName: string;
    textContent: string;

    /**
     * A text node's content, which is what the morph compares and assigns for nodeType 3 — where the
     * diff codec uses `textContent` for the same node. Both are here because the framework reads
     * both, on the same objects.
     */
    nodeValue: string | null;
    parentNode: StubParent | null;
    nextSibling: StubNode | null;
    previousSibling: StubNode | null;
    firstChild: StubNode | null;
}

/** The attribute surface every stub element carries. */
export interface StubAttributes {
    tagName: string;

    hasAttribute(name: string): boolean;
    getAttribute(name: string): string | null;
    setAttribute(name: string, value: string): void;
    removeAttribute(name: string): void;
    readonly attributes: { name: string; value: string }[];
}

/**
 * A leaf form control: an input, an option, a textarea.
 *
 * Separate from StubParent because these have no children and the guards under test are entirely
 * about the split between an attribute and the live property beside it — which is the state a
 * browser reaches on user interaction and a test cannot otherwise produce.
 */
export interface StubControl extends StubNode, StubAttributes {
    /** The live property. Independent of the `value` attribute once the dirty-value flag is set. */
    value: string;

    /** Flipped natively by a click, which never touches the `checked` attribute. */
    checked: boolean;

    /** Moved by the select, which never touches the `selected` attribute. */
    selected?: boolean;
}

/** A node that has children — in these stubs, every element that is not a leaf control. */
export interface StubParent extends StubNode, StubAttributes {

    /**
     * The child list, left visible.
     *
     * A stub exists to be inspectable where a real element is not: the scenarios read this to check
     * what the morph did to the tree.
     */
    _kids: StubNode[];

    innerHTML: string;

    // Real insertBefore/removeChild semantics, including the throw when the reference node is not a
    // child — which is what surfaces the anchor-staleness bug one of the fixtures pins.
    insertBefore(node: StubNode, ref: StubNode | null): StubNode;
    appendChild(node: StubNode): StubNode;
    removeChild(node: StubNode): StubNode;

    // Optional because only the fixtures whose scenario reaches these paths implement them: the diff
    // codec's resolvePath walks `childNodes`, the morph swaps a subtree with `replaceChild` and
    // constructs from `cloneNode`, and the select guard asks an option for its `closest("select")`.
    // A fixture that does not exercise one leaves it out rather than writing a stub nothing calls.
    childNodes?: StubNode[];
    replaceChild?(newNode: StubNode, oldNode: StubNode): StubNode;
    cloneNode?(deep?: boolean): StubNode;
    closest?(selector: string): StubNode | null;
}

/**
 * Hands a stub to the framework.
 *
 * The one cast, and it is at the right seam: the moment a fake crosses into code that believes it is
 * a DOM node. Everything on the fixture's side of this call stays a stub, mutable and inspectable;
 * everything on the far side is checked against the real interface.
 *
 * The alternative — having the factories return `HTMLElement` outright — reads as tidier and is
 * worse: it puts the fiction where the object is built, so the fixture's own tree manipulation then
 * fights the browser's readonly `parentNode`, and every honest stub operation needs a cast instead.
 */
export function asDom<T extends Node>(node: StubNode): T {
    return node as unknown as T;
}

/** What a fixture supplies as `document`. Anything a module under test reads must be present. */
export interface StubDocument {
    /**
     * The element the morph's focus guard compares against. Fixtures set this to a spectator element
     * so the value-sync path runs, which is the realistic state after a change commits on blur.
     */
    activeElement: unknown;

    head?: unknown;

    createElement?: (tagName?: string) => unknown;

    /** Radio grouping asks the document for the whole group by selector. */
    querySelectorAll?: (selector: string) => readonly unknown[];
}

/**
 * Installs the stub as the module-global `window` and `document`.
 *
 * `window` is `globalThis` itself, matching the browser, so a module doing
 * `window.__raskSomething = …` and a later `globalThis.__raskSomething` read see one object.
 */
export function installStubGlobals(document: StubDocument): void {
    const globals = globalThis as unknown as { window: unknown; document: unknown };
    globals.window = globalThis;
    globals.document = document;
}

/**
 * Narrows a child to a parent-capable node.
 *
 * A stub's child list holds text nodes as well as elements, so reading an attribute off one is a
 * claim about which it is. Named, so the claim is visible where it is made and fails loudly when
 * wrong, rather than hiding in an inline cast.
 */
export function asStubParent(node: StubNode | null): StubParent {
    if (!node || node.nodeType !== 1) {
        throw new Error(`expected an element, got nodeType ${node ? node.nodeType : "null"}`);
    }

    return node as StubParent;
}
