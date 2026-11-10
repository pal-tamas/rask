// A picture of a file the reader chose, drawn from the file itself: no bytes go anywhere for it.
//
// An <img data-rask-preview="<size>:<lastModified>:<name>"> (Ui.FileItem's `.Preview(file)`) is filled with an
// object URL of the image the reader chose with those facts — the three a page holds of a file after its handler
// is done, on either host. The image is the browser's own file, so a huge one costs no copy; a file that is no
// image, or one this tab never saw chosen, leaves the <img> without a source, and the item shows its icon.
//
// KEPT AND LET GO. The images chosen are kept here, the last few dozen, for the items a page draws of them. Each
// object URL lives exactly as long as the <img> that shows it: when the <img> leaves the page, or is drawn for
// another file, the URL is revoked. The `src` is the runtime's, not a render's (rask-owned.ts `own`).

import {disown, own, page} from "./rask-owned.js";

const PREVIEW = "data-rask-preview";
const KEPT_AT_MOST = 48;

/** The images chosen, by size and time: a name is matched among those, since a server may tidy it. */
const chosen = new Map<string, File[]>();
let count = 0;

/** What each filled <img> shows: the URL, and the file it was drawn for. */
const shown = new Map<Element, {url: string; key: string}>();

let watching = false;

function sizeAndTime(size: number, lastModified: number): string {
    return size + ":" + lastModified;
}

/** The chosen file the attribute's value names, if this tab saw it chosen. */
export function fileFor(key: string): File | null {
    const first = key.indexOf(":");
    const second = key.indexOf(":", first + 1);
    if (first < 0 || second < 0) return null;
    const same = chosen.get(key.slice(0, second));
    if (!same || same.length === 0) return null;
    const name = key.slice(second + 1);
    for (const file of same) {
        if (file.name === name) return file;
    }
    return same[same.length - 1];
}

/** Keeps the images among `files` for the previews a page draws of them. */
export function remember(files: ArrayLike<File>): void {
    let any = false;
    for (let i = 0; i < files.length; i++) {
        const file = files[i];
        if (!/^image\//i.test(file.type || "")) continue;
        const key = sizeAndTime(file.size, file.lastModified || 0);
        const same = chosen.get(key) || [];
        if (same.indexOf(file) < 0) {
            same.push(file);
            count++;
        }
        chosen.delete(key); // newest last
        chosen.set(key, same);
        any = true;
    }
    while (count > KEPT_AT_MOST) {
        const oldest = chosen.keys().next().value as string;
        count -= chosen.get(oldest)!.length;
        chosen.delete(oldest);
    }
    if (any) {
        watch();
        refresh();
    }
}

function letGo(img: Element): void {
    const was = shown.get(img);
    if (!was) return;
    URL.revokeObjectURL(was.url);
    shown.delete(img);
    if (img.isConnected && img.getAttribute("src") === was.url) img.removeAttribute("src");
    disown(img, "src");
}

function fill(img: Element): void {
    const key = img.getAttribute(PREVIEW);
    const was = shown.get(img);
    if (was && was.key === key) return;
    letGo(img);
    const file = key ? fileFor(key) : null;
    if (!file) return;
    const url = URL.createObjectURL(file);
    shown.set(img, {url, key: key!});
    own(img, "src");
    img.setAttribute("src", url);
}

/** Lets go of every URL whose <img> has gone, and fills every <img> that asks for a file. */
export function refresh(): void {
    for (const img of Array.from(shown.keys())) {
        if (!img.isConnected || !img.hasAttribute(PREVIEW)) letGo(img);
    }
    if (!page) return;
    const asking = page.querySelectorAll("img[" + PREVIEW + "]");
    for (let i = 0; i < asking.length; i++) fill(asking[i]);
}

/** How many object URLs are alive: what a test reads to see that none leaked. */
export function liveUrls(): number {
    return shown.size;
}

// Watched only once a file was chosen: a page that never shows a preview runs no observer for one.
function watch(): void {
    if (watching || !page || typeof MutationObserver !== "function") return;
    watching = true;
    new MutationObserver(refresh).observe(page.documentElement, {subtree: true, childList: true, attributes: true, attributeFilter: [PREVIEW]});
}

if (page) {
    page.addEventListener("change", function (e) {
        const input = e.target as HTMLInputElement | null;
        if (input && input.tagName === "INPUT" && input.type === "file" && input.files) remember(input.files);
    }, true);
}
