// Scoped TypeScript for GuideChrome, exposed as Rask.GuideChrome.spy / .stop. Everything here is
// client-only — scroll-spy highlighting and smooth in-page scrolling never touch the server. The C#
// side hands over the guide root ElementRef (resolved to the live element by the runtime reviver) on
// mount, and again on unmount so we can disconnect the observer. State is kept off the DOM in a
// WeakMap keyed by the root, so SPA navigation between guides sets up a fresh observer and drops the
// old one without leaking.

/** What `spy` set up for one guide root, so `stop` can take it all down again. */
interface GuideState {
    observer: IntersectionObserver | null;
    links: Map<string, HTMLAnchorElement[]>;
}

const state = new WeakMap<HTMLElement, GuideState>();

// Highlights the "On this page" / Chapters link whose section is currently in view, and wires smooth
// scrolling for every in-page anchor. Idempotent per root (a re-mount replaces the previous observer).
export function spy(root: HTMLElement | null): void {
    if (!root) {
        return;
    }

    stop(root);

    // Every anchor that points at an in-page section, keyed by its target id.
    const links = new Map<string, HTMLAnchorElement[]>();
    root.querySelectorAll<HTMLAnchorElement>('a[href^="#"]').forEach((a) => {
        // The selector guarantees the attribute is present, but getAttribute is still typed
        // string | null — and `?? ''` here is what keeps the empty-id guard below meaningful
        // rather than a crash one line earlier.
        const id = decodeURIComponent((a.getAttribute('href') ?? '').slice(1));
        if (!id) {
            return;
        }

        let anchors = links.get(id);
        if (!anchors) {
            anchors = [];
            links.set(id, anchors);
        }

        anchors.push(a);
        // Smooth-scroll in-page instead of letting the SPA router or a hard jump handle the hash.
        a.addEventListener('click', onAnchorClick);
    });

    // On a fresh load / refresh of a URL carrying a "#fragment" (a shared deep link), jump to that
    // section. The click path above handles in-SPA anchor clicks; this covers a hard navigation where
    // the browser's own jump fired before the guide body (prose, highlighted code, live demos) had
    // rendered, so it landed nowhere. Runs on every mount; a no-op when there is no hash / no target.
    scrollToHash();

    const headings = Array.from(root.querySelectorAll<HTMLElement>('.markdown-body :is(h2, h3)[id]'));
    if (headings.length === 0) {
        state.set(root, { observer: null, links });
        return;
    }

    // Track which headings are on screen; the topmost visible one is the "current" section.
    const onScreen = new Set<Element>();
    const setActive = (id: string): void => {
        links.forEach((anchors, key) => {
            const on = key === id;
            anchors.forEach((a) => a.classList.toggle('active', on));
        });
    };

    const observer = new IntersectionObserver(
        (entries) => {
            for (const entry of entries) {
                if (entry.isIntersecting) {
                    onScreen.add(entry.target);
                } else {
                    onScreen.delete(entry.target);
                }
            }

            // Pick the first heading (document order) that is currently intersecting.
            const current = headings.find((h) => onScreen.has(h));
            if (current) {
                setActive(current.id);
            }
        },
        // Bias the band toward the top of the viewport so the highlight tracks what you're reading.
        { rootMargin: '0px 0px -70% 0px', threshold: 0 });

    headings.forEach((h) => observer.observe(h));
    state.set(root, { observer, links });
}

// Disconnects the observer and unbinds the anchor handlers for a guide root (called on unmount).
export function stop(root: HTMLElement | null): void {
    const entry = root ? state.get(root) : undefined;
    if (!entry || !root) {
        return;
    }

    if (entry.observer) {
        entry.observer.disconnect();
    }
    entry.links.forEach((anchors) => anchors.forEach((a) => a.removeEventListener('click', onAnchorClick)));
    state.delete(root);
}

function scrollToHash(): void {
    const id = decodeURIComponent(location.hash.slice(1));
    if (!id) {
        return;
    }
    const target = document.getElementById(id);
    if (!target) {
        return;
    }
    // Two rAFs: let the just-mounted guide body (and any co-mounted live demos) finish laying out
    // before we measure, so the jump lands on the section rather than a pre-layout position. The
    // heading's scroll-margin-top (global.css) clears the sticky navbar.
    requestAnimationFrame(() => requestAnimationFrame(() => target.scrollIntoView({ block: 'start' })));
}

function onAnchorClick(event: Event): void {
    // currentTarget is EventTarget | null on the base Event; this handler is only ever bound to the
    // anchors collected above, so the narrowing is a statement of that fact rather than a guess.
    const anchor = event.currentTarget as HTMLAnchorElement | null;
    const href = anchor?.getAttribute('href');
    if (!href || href.charAt(0) !== '#') {
        return;
    }

    const id = decodeURIComponent(href.slice(1));
    const target = document.getElementById(id);
    if (!target) {
        return;
    }

    event.preventDefault();
    target.scrollIntoView({ behavior: 'smooth', block: 'start' });
    // Keep the URL fragment in sync without adding a history entry per click.
    if (history.replaceState) {
        history.replaceState(null, '', `#${id}`);
    }
}
