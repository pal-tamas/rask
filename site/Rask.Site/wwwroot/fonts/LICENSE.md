# Fonts shipped with rask.sh

These three families are vendored here rather than loaded from a font CDN, so the site's text is
drawn in its real face on the first paint and never reflows mid-page. See the `@font-face` block at
the top of `wwwroot/global.css` for why (a deferred cross-origin stylesheet fought the framework's
document morph and reflowed the whole page at hydration).

Each file is the **variable** `woff2` Google Fonts serves for a modern browser — one file per family
covers every weight the site uses — subset to `latin` and `latin-ext`.

| Family | Files | Weights | Upstream |
| --- | --- | --- | --- |
| Inter | `inter-latin.woff2`, `inter-latin-ext.woff2` | 400–700 | <https://github.com/rsms/inter> |
| Space Grotesk | `space-grotesk-latin.woff2`, `space-grotesk-latin-ext.woff2` | 500–700 | <https://github.com/floriankarsten/space-grotesk> |
| JetBrains Mono | `jetbrains-mono-latin.woff2`, `jetbrains-mono-latin-ext.woff2` | 400–600 | <https://github.com/JetBrains/JetBrainsMono> |

All three are licensed under the **SIL Open Font License, Version 1.1**
(<https://openfontlicense.org/>), which permits redistribution and self-hosting, bundled with other
software, provided the fonts are not sold on their own and the licence travels with them. The full
licence text ships with each upstream project at the URLs above.

The families are **not** renamed, and are used as regular web fonts — neither of the OFL conditions
that would require a Reserved Font Name change applies.

## Refreshing them

Ask the Google Fonts CSS API for the variable ranges with a modern browser `User-Agent`, then
download the `latin` and `latin-ext` `src` URLs it names:

```
https://fonts.googleapis.com/css2?family=Inter:wght@400..700&family=Space+Grotesk:wght@500..700&family=JetBrains+Mono:wght@400..600&display=swap
```

Keep the weight ranges in `global.css` in step with whatever that CSS reports.
