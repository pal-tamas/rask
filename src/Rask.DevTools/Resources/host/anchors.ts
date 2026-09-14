// Where the Tree tab says its nodes are, and which of them a node on the page belongs to. Pure: no DOM, so the matching
// a pick depends on is testable on its own.
//
// A place is `path|firstSlot|count`, the coordinates the diff patches by: the child slots from the document to the
// parent (dot-separated, empty at the top), the slot the first node occupies, and how many sibling nodes follow it.

/** A node's place on the page. */
export interface Place {
    readonly path: readonly number[];
    readonly first: number;
    readonly count: number;
}

/** One node of the tree a pick can land on. */
export interface Anchor {
    readonly id: string;
    readonly place: Place;
    readonly label: string;
}

/** Reads a place, or null for anything malformed — a pick must never throw on the page it is inspecting. */
export function parsePlace(at: string | null | undefined): Place | null {
    if (typeof at !== "string") return null;
    const parts = at.split("|");
    if (parts.length !== 3) return null;
    const path = parts[0] === "" ? [] : parts[0].split(".").map(Number);
    const first = Number(parts[1]);
    const count = Number(parts[2]);
    const whole = (n: number) => Number.isInteger(n) && n >= 0;
    if (!path.every(whole) || !whole(first) || !whole(count) || count === 0) return null;
    return {path, first, count};
}

/** Reads the Tree tab's `[[id, at, label], …]`, keeping only the entries that parse. */
export function parseAnchors(json: string | null | undefined): Anchor[] {
    if (typeof json !== "string") return [];
    let rows: unknown;
    try {
        rows = JSON.parse(json);
    } catch {
        return [];
    }
    if (!Array.isArray(rows)) return [];
    const anchors: Anchor[] = [];
    for (const row of rows) {
        if (!Array.isArray(row) || typeof row[0] !== "string" || typeof row[2] !== "string") continue;
        const place = parsePlace(row[1]);
        if (place) anchors.push({id: row[0], place, label: row[2]});
    }
    return anchors;
}

/** Whether the node at `nodePath` is one of the place's nodes or inside one. */
export function contains(place: Place, nodePath: readonly number[]): boolean {
    const depth = place.path.length;
    if (nodePath.length <= depth) return false;
    for (let i = 0; i < depth; i++) {
        if (nodePath[i] !== place.path[i]) return false;
    }
    const slot = nodePath[depth];
    return slot >= place.first && slot < place.first + place.count;
}

/**
 * The anchor a node belongs to: the deepest place that contains it. Where two places are equally deep — a component
 * whose whole output is one child component's, or a component and the element it rendered — the one later in the list
 * wins, and the Tree tab lists its nodes parents first, so that is the node further down the tree.
 */
export function findAnchor(anchors: readonly Anchor[], nodePath: readonly number[]): Anchor | null {
    let best: Anchor | null = null;
    for (const anchor of anchors) {
        if (!contains(anchor.place, nodePath)) continue;
        if (!best || anchor.place.path.length >= best.place.path.length) best = anchor;
    }
    return best;
}
