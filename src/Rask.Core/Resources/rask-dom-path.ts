// The positional addressing the diff codec shares with everything that has to find a node the server named: which of a
// parent's child nodes the server's frame walk counts, and the path from `document` to a node.
//
// A module of its own, with NO side effects, so a bundle can import it without also running what rask-dom.ts and
// rask-morph.ts install when they load (the focus trap, popover and reload listeners, the head observer). The Rask
// DevTools host script is that bundle: it runs on the app's page beside the runtime, and has to resolve the same paths
// the runtime patches without binding a second set of document listeners.

/**
 * Narrows a node to an Element — which is exactly what `nodeType === 1` means.
 *
 * Replaces the `n.nodeType === 1 && n.getAttribute` pattern the morph used in four places: the
 * second half was duck-typing standing in for a type system, and on a real Element it is never false.
 */
export function isElement(n: Node | null | undefined): n is Element {
    return !!n && n.nodeType === 1;
}

/**
 * True for a text node that is nothing but whitespace.
 */
export function isFormattingText(n: Node): boolean {
    return n.nodeType === 3 && !/\S/.test(n.nodeValue || "");
}

/**
 * Whether whitespace between this element's children is pure formatting, and so may be ignored when
 * pairing the two sides of a morph.
 *
 * Only <html> and <head>, and deliberately not "any element": whitespace BETWEEN INLINE ELEMENTS is
 * rendered -- `<p>a <b>b</b> c</p>` -- so a blanket filter would pair around real, visible nodes and
 * silently drop them. These two are where a document PARSED from HTML meets a payload SERIALIZED by
 * HtmlSerializer, which is the only place the two sides can disagree about whitespace at all.
 *
 * The case that forced this: a prerendered page is served with a newline between `</head>` and
 * `<body>`, and the parser puts that text node in <html> (the "after head" insertion mode). The live
 * <html> therefore had [HEAD, #text, BODY] while the runtime's full-frame payload had [HEAD, BODY],
 * so the positional walk paired #text against BODY, found the names different, and REPLACED the body
 * with a brand-new element. A fresh <body> has no resolved style yet, so hydration painted one
 * completely unstyled frame -- UA serif on a transparent ground, at 13x the height.
 */
export function ignoresFormattingText(el: Element): boolean {
    return el.nodeName === "HTML" || el.nodeName === "HEAD";
}

// Comment nodes shift childNodes indices relative to the server's frame walk.
// Filter to DOM-relevant nodes only (Element=1, Text=3, Doctype=10) so paths
// match what FrameDiffer counts.
export const _relevantNodeTypes: Record<number, number> = {1: 1, 3: 1, 10: 1};

// Whether `n` is a node the server's frame walk cannot see, and so must not be counted when
// resolving a path under `parent`.
//
// These are the same two filters morph() applies when it pairs the two sides of a subtree, and for
// the same reason: counting a node that is in no payload shifts every following sibling by one, so
// the op lands on the node next door — or, when the miss is a text node an op cannot use, is dropped
// in complete silence.
//
//  * `data-rask-managed` marks a node the BROWSER added (a library's injected <style>, the hot-reload
//    pill). The server never emits the marker, so such a node appears in no frame walk.
//  * Formatting whitespace inside <html>/<head> (see ignoresFormattingText): a document PARSED from
//    HTML and a payload SERIALIZED by HtmlSerializer are allowed to disagree there. A shell served
//    with a newline between `</head>` and `<body>` parses to [HEAD, #text, BODY] while the frame walk
//    counts [HEAD, BODY], so BODY arrives as slot 1 and used to resolve to the newline.
export function invisibleToFrameWalk(parent: Node, n: Node): boolean {
    if (isElement(n) && n.hasAttribute("data-rask-managed")) return true;
    return isElement(parent) && ignoresFormattingText(parent) && isFormattingText(n);
}

export function relevantChild(parent: Node | null, index: number): Node | null {
    if (!parent || !parent.childNodes) return null;
    let seen = 0;
    for (const n of parent.childNodes) {
        if (invisibleToFrameWalk(parent, n)) continue;
        if (_relevantNodeTypes[n.nodeType]) {
            if (seen === index) return n;
            seen++;
        }
    }
    return null;
}

export function resolvePath(path: number[]): Node | null {
    let node: Node | null = document;
    for (const slot of path) {
        node = relevantChild(node, slot);
        if (!node) return null;
    }
    return node;
}

/**
 * The path `resolvePath` would take to reach `node`: at each level, its index among the parent's child nodes the frame
 * walk counts. Null for a node the walk does not count, or one not in `document`.
 */
export function nodePath(node: Node): number[] | null {
    const path: number[] = [];
    let current: Node = node;
    while (current !== document) {
        const parent = current.parentNode;
        if (!parent || !_relevantNodeTypes[current.nodeType] || invisibleToFrameWalk(parent, current)) return null;
        let index = 0;
        for (const n of parent.childNodes) {
            if (n === current) break;
            if (invisibleToFrameWalk(parent, n)) continue;
            if (_relevantNodeTypes[n.nodeType]) index++;
        }
        path.push(index);
        current = parent;
    }
    return path.reverse();
}
