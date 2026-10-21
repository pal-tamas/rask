// Which attributes a patch may take away. A patch removes an attribute only when the server wrote it: one the page
// put on a rendered element itself — a canvas's width and height, set by the script that draws on it — was never
// in a render, and a render that leaves it out says nothing about it. The diff already holds to this (it removes
// only what the last render wrote); the morph reconciles against fresh markup, so it cannot tell, and asks here.
//
// What the server writes reaches the page as the parsed document, as fragments inserted, and as the attribute
// writes of a patch. The first two are no attribute mutation at all, and the third runs between `beginPatch` and
// `endPatch`, whose records are dropped. Any other attribute that appears where there was none is the page's, until
// a render writes it.
//
// On the global, like rask-owned.ts's seam: the runtime and the hooks are separate bundles, and two watches would
// each count the other's patches as the page's.

interface Written {
    /** What the page added to each element, by name. */
    added: WeakMap<Element, Set<string>>;
    /** The attribute watch; null where there is no document to watch. */
    watch: MutationObserver | null;
    /** How many patches are running, one inside another. */
    depth: number;
}

const scope = globalThis as typeof globalThis & {__raskWritten?: Written};

function heard(records: MutationRecord[]): void {
    for (const record of records) {
        const name = record.attributeName;
        const el = record.target as Element;
        if (name !== null && record.oldValue === null && el.hasAttribute(name)) {
            let names = written.added.get(el);
            if (!names) {
                written.added.set(el, names = new Set());
            }
            names.add(name);
        }
    }
}

function watchPage(): MutationObserver | null {
    if (typeof MutationObserver === "undefined" || typeof document === "undefined") {
        return null;
    }
    const watch = new MutationObserver(heard);
    watch.observe(document, {attributes: true, attributeOldValue: true, subtree: true});
    return watch;
}

const written: Written = scope.__raskWritten
    || (scope.__raskWritten = {added: new WeakMap<Element, Set<string>>(), watch: watchPage(), depth: 0});

/** A patch begins: what the page wrote up to now is the page's, and what the patch writes is the server's. */
export function beginPatch(): void {
    if (written.depth++ === 0 && written.watch) {
        heard(written.watch.takeRecords());
    }
}

/** The patch is done: the attributes it wrote were the server's. */
export function endPatch(): void {
    if (--written.depth === 0 && written.watch) {
        written.watch.takeRecords();
    }
}

/** A render wrote attribute `name` of `el`: from now on it is the server's to take away. */
export function serverWrote(el: Element, name: string): void {
    const names = written.added.get(el);
    if (names) {
        names.delete(name);
    }
}

/** Whether attribute `name` of `el` is one the page added and no render has written since. */
export function pageAdded(el: Element, name: string): boolean {
    const names = written.added.get(el);
    return names !== undefined && names.has(name);
}

/** Notes, outside any watch, that the page added attribute `name` to `el` — what the watch does, for a test. */
export function notePageAdded(el: Element, name: string): void {
    heard([{attributeName: name, oldValue: null, target: el} as unknown as MutationRecord]);
}
