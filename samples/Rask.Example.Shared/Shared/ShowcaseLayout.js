// Scoped JS for ShowcaseLayout, exposed as Rask.ShowcaseLayout.toggleTheme. Flips the color theme by
// stamping BOTH data-theme (the raw --ground/--accent/... tokens) and data-bs-theme (Bootstrap 5.3) on
// <html> together, then persists the choice in localStorage. Because the site, docs and playground share
// one origin, that key carries the theme across all three. The pre-boot default is set by the inline
// snippet in <head>/index.html; this only handles the explicit toggle.

export function toggleTheme() {
    const d = document.documentElement;
    let cur = d.getAttribute('data-theme');
    if (!cur) {
        cur = matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    }
    const next = cur === 'dark' ? 'light' : 'dark';
    d.setAttribute('data-theme', next);
    d.setAttribute('data-bs-theme', next);
    try {
        localStorage.setItem('rask-theme', next);
    } catch (e) {
        // Storage can throw in private mode / when blocked — the toggle still works for this session.
    }
}
