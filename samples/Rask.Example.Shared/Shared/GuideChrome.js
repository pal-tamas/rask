// Scoped JS for GuideChrome, exposed as Rask.GuideChrome.spy / .stop. Everything here is client-only —
// scroll-spy highlighting and smooth in-page scrolling never touch the server. The C# side hands over
// the guide root ElementRef (resolved to the live element by the runtime reviver) on mount, and again on
// unmount so we can disconnect the observer. State is kept off the DOM in a WeakMap keyed by the root, so
// SPA navigation between guides sets up a fresh observer and drops the old one without leaking.

const state = new WeakMap();

// Highlights the "On this page" / Chapters link whose section is currently in view, and wires smooth
// scrolling for every in-page anchor. Idempotent per root (a re-mount replaces the previous observer).
export function spy(root) {
    if (!root) {
        return;
    }

    stop(root);

    // Every anchor that points at an in-page section, keyed by its target id.
    const links = new Map();
    root.querySelectorAll('a[href^="#"]').forEach((a) => {
        const id = decodeURIComponent(a.getAttribute('href').slice(1));
        if (!id) {
            return;
        }
        if (!links.has(id)) {
            links.set(id, []);
        }
        links.get(id).push(a);
        // Smooth-scroll in-page instead of letting the SPA router or a hard jump handle the hash.
        a.addEventListener('click', onAnchorClick);
    });

    const headings = Array.from(root.querySelectorAll('.markdown-body :is(h2, h3)[id]'));
    if (headings.length === 0) {
        state.set(root, { observer: null, links });
        return;
    }

    // Track which headings are on screen; the topmost visible one is the "current" section.
    const onScreen = new Set();
    const setActive = (id) => {
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
export function stop(root) {
    const entry = root && state.get(root);
    if (!entry) {
        return;
    }

    if (entry.observer) {
        entry.observer.disconnect();
    }
    entry.links.forEach((anchors) => anchors.forEach((a) => a.removeEventListener('click', onAnchorClick)));
    state.delete(root);
}

function onAnchorClick(event) {
    const href = event.currentTarget.getAttribute('href');
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
