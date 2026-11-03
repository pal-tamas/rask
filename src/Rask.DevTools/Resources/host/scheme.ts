// Light or dark for the devtools' own chrome, as the kit decides it for the page around them.
//
// The kit (Ui.AppearanceScript) states dark mode as a `dark` class on <html>, and that class is the whole answer on a
// page that carries the script: present is dark, absent is light, whatever the system says. A page without the script
// states nothing, and the dock follows prefers-color-scheme — in its CSS, so no listener is needed for that case.
//
// The answer goes on the dock's host element as data-scheme ("dark", "light", or absent for "the system's"), which the
// shadow root's stylesheet reads with :host([data-scheme=...]). Attributes, never an inline style: a strict CSP stays
// strict.

export type Scheme = "dark" | "light";

/** The attribute on the dock's host element that carries what the page states. */
export const SCHEME_ATTRIBUTE = "data-scheme";

/** What the page states about its scheme: its `dark` class, else light on a kit page, else nothing (the system's). */
export function pageScheme(doc: Pick<Document, "documentElement" | "querySelector">): Scheme | null {
    if (doc.documentElement.classList.contains("dark")) return "dark";
    return doc.querySelector("script[data-rask-appearance]") ? "light" : null;
}

/** Keeps `host`'s data-scheme in step with the page, now and whenever <html>'s class changes. */
export function followPageScheme(host: HTMLElement): void {
    // A document without MutationObserver is not a browser's (the unit fixtures' stub DOM): the system's scheme then.
    if (typeof MutationObserver !== "function") return;

    const apply = () => {
        const scheme = pageScheme(document);
        if (scheme) {
            host.setAttribute(SCHEME_ATTRIBUTE, scheme);
        } else {
            host.removeAttribute(SCHEME_ATTRIBUTE);
        }
    };
    apply();
    new MutationObserver(apply).observe(document.documentElement, {attributes: true, attributeFilter: ["class"]});
}
