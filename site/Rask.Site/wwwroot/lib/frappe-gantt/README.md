# frappe-gantt (vendored)

Third-party library, vendored verbatim — **do not edit these files**.

| | |
|---|---|
| Upstream | <https://github.com/frappe/gantt> |
| Version | v1.0.3 |
| License | MIT — see `license.txt` (Copyright (c) 2024 Frappe Technologies Pvt. Ltd.) |
| Files | `frappe-gantt.umd.js` (sets the `Gantt` global), `frappe-gantt.css` |
| Fetched from | `https://unpkg.com/frappe-gantt@1.0.3/dist/` |

Vendored rather than loaded from a CDN so the showcase works offline and under the GitHub Pages
sub-path.

Used by `Features/Gantt/Gantt.cs` + `Gantt.js` — the third-party-interop demo embedded in
`docs/js-interop.md`. To upgrade, re-download both files at the new tag, refresh the version above,
and re-run the Gantt E2E steps (the wrapper depends on the `on_click` / `on_date_change` /
`on_progress_change` callback names and the `Quarter Day` / `Half Day` view-mode strings, none of
which upstream documents in its README).
