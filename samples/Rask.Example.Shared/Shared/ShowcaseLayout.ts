// Scoped TypeScript for ShowcaseLayout, exposed as Rask.ShowcaseLayout.toggleTheme. Flips the color
// theme by stamping BOTH data-theme (the raw --ground/--accent/... tokens) and data-bs-theme
// (Bootstrap 5.3) on <html> together, then persists the choice in localStorage. Because the site,
// docs and playground share one origin, that key carries the theme across all three. The pre-boot
// default is set by the inline snippet in <head>/index.html; this only handles the explicit toggle.

type Theme = 'dark' | 'light';

export function toggleTheme(): void {
    const d = document.documentElement;

    // getAttribute returns string | null, and the null branch is real: before the first toggle the
    // attribute is whatever the pre-boot snippet stamped, which is nothing when the visitor has no
    // stored preference.
    let cur = d.getAttribute('data-theme');
    if (!cur) {
        cur = matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    }

    const next: Theme = cur === 'dark' ? 'light' : 'dark';
    d.setAttribute('data-theme', next);
    d.setAttribute('data-bs-theme', next);

    try {
        localStorage.setItem('rask-theme', next);
    } catch {
        // Storage can throw in private mode / when blocked — the toggle still works for this session.
    }
}
