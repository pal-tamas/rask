// A navigation's history entry, and a move between #fragments of the page on show — the same on both hosts (#1310).
//
// A plain link to "#section", and Back or Forward between two fragments of one page, are the browser's own: it
// writes or walks the entry, scrolls to the anchor (or back to where the reader was) and fires `popstate`. Nothing
// about the page changed, so neither host asks .NET for it again — and the replace a render answers with, which
// writes the address .NET knows and never the fragment, would drop the one the reader just followed.
//
// No DOM access at import: the address on show is read when a host says it is showing one.

let shown: string | null = null;

function address(): string {
    return location.pathname + location.search;
}

/** The page at the current address is the one on show: called once, as the runtime starts. */
export function addressShown(): void {
    shown = address();
}

/**
 * Asked of a `popstate`: true when the entry it moved to differs from the page on show in nothing but its
 * #fragment, which is the browser's to scroll. Otherwise its address is the page about to be shown, and false.
 */
export function onlyFragmentMoved(): boolean {
    const now = address();
    if (now === shown) return true;
    shown = now;
    return false;
}

/**
 * Writes a navigation's history entry. A push adds the clicked link's #fragment; a replace keeps the entry's own
 * when it names the page already at the address (Back to "/page#section" from another page).
 */
export function writeEntry(target: string, replace: boolean, pushedHash: string): void {
    if (replace) {
        history.replaceState({rask: true}, "", target === address() ? target + location.hash : target);
    } else {
        history.pushState({rask: true}, "", target + pushedHash);
    }
    shown = address();
}
