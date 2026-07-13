# Vendored Monaco Editor

A minimal subset of [monaco-editor](https://github.com/microsoft/monaco-editor) **v0.52.2**, the code
editor that powers the playground. Vendored (rather than loaded from a CDN) so the playground is
self-contained and works offline / under the GitHub Pages sub-path.

Only what C# syntax highlighting needs is included — `vs/loader.js`, `vs/editor`, `vs/base`, and
`vs/basic-languages/csharp` — not the heavy TypeScript/JSON/CSS/HTML language services (`vs/language`) or
the non-English locale bundles.

Copied from the npm package's `min/vs`:

```bash
npm i monaco-editor@0.52.2
cp -R node_modules/monaco-editor/min/vs/{loader.js,editor,base} wwwroot/lib/monaco/vs/
cp node_modules/monaco-editor/min/vs/basic-languages/csharp/csharp.js wwwroot/lib/monaco/vs/basic-languages/csharp/
```

Monaco Editor is © Microsoft Corporation, licensed under the
[MIT License](https://github.com/microsoft/monaco-editor/blob/main/LICENSE.md).
