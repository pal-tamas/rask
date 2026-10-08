#!/usr/bin/env node
// Bundles the editor engine: ui-editor.ts + Tiptap (pinned in package.json, locked in package-lock.json)
// into ONE minified ES module, ../ui-editor.js, which is committed. The kit's build never runs this —
// it needs npm, and the kit builds without Node — so run it by hand after changing ui-editor.ts or a pin:
//
//     cd src/Rask.Ui/Resources/editor && npm ci && node build.mjs
//
// It also writes ../ui-editor.LICENSES.txt — the notice of every package in the bundle, and of Lucide, whose
// path data the toolbar's icons are drawn from in C# (UiEditorIcons.cs) — and the content hash
// UiEditorEngine.Version carries, which UiEditorTests holds to the committed bundle.
//
// notices/ holds the two licence texts npm does not deliver: Tiptap's packages ship none (the text is the
// LICENSE.md of ueberdosis/tiptap at the pinned tag), and Lucide is not a dependency of this bundle at all
// (lucide-icons/lucide at 0.300.0). Refresh them when a pin moves.
import { build } from 'esbuild';
import { createHash } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const out = join(here, '..', 'ui-editor.js');
const banner = '/*! Rask.Ui editor engine. Bundles Tiptap and ProseMirror (MIT) — see rask-ui-editor.LICENSES.txt, beside this file. */';
const result = await build({
  entryPoints: [join(here, 'ui-editor.ts')],
  outfile: out,
  bundle: true,
  format: 'esm',
  target: 'es2022',
  minify: true,
  legalComments: 'none',
  banner: { js: banner },
  metafile: true,
  logLevel: 'warning',
});

// Every package that contributed bytes, with the licence text it ships.
const packages = new Map();
for (const input of Object.keys(result.metafile.outputs[relative(process.cwd(), out)]?.inputs ?? Object.values(result.metafile.outputs)[0].inputs)) {
  const match = /node_modules\/((?:@[^/]+\/)?[^/]+)\//.exec(input);
  if (match) packages.set(match[1], join(here, 'node_modules', match[1]));
}

const section = (title, text) => `\n${'='.repeat(78)}\n${title}\n${'='.repeat(78)}\n${text.trim()}\n`;
const kept = name => readFile(join(here, 'notices', name), 'utf8');
let notices = 'Third-party notices of Ui.Editor.\n\nThe editor engine (ui-editor.js) bundles the following packages.\n';
for (const [name, directory] of [...packages].sort(([a], [b]) => a.localeCompare(b))) {
  const manifest = JSON.parse(await readFile(join(directory, 'package.json'), 'utf8'));
  let text = '';
  for (const file of ['LICENSE', 'LICENSE.md', 'LICENSE.txt', 'license', 'license.md']) {
    text = await readFile(join(directory, file), 'utf8').catch(() => '');
    if (text) break;
  }

  // Tiptap publishes its packages without the licence file its repository carries.
  if (!text && name.startsWith('@tiptap/')) text = await kept('tiptap.LICENSE.md');
  if (!text) throw new Error(`${name} ${manifest.version} (${manifest.license}) ships no licence file: add its text to notices/ and name it here.`);
  notices += section(`${name} ${manifest.version} — ${manifest.license}`, text);
}

notices += '\n\nEleven toolbar icons (Lucide\'s type, list-ordered, text-quote, link-2, unlink, undo, redo, subscript,\n'
  + 'superscript, highlighter and code) are drawn by the kit\'s assembly, Rask.Ui.dll, from the path data of:\n'
  + section('lucide 0.300.0 — ISC', await kept('lucide.LICENSE'));

await writeFile(join(here, '..', 'ui-editor.LICENSES.txt'), notices);
const bytes = await readFile(out);
const version = createHash('sha256').update(bytes).digest('hex').slice(0, 8);
const source = join(here, '..', '..', 'UiEditorEngine.cs');
const text = await readFile(source, 'utf8');
await writeFile(source, text.replace(/(public const string Version = ")[0-9a-f]*(";)/, `$1${version}$2`));
console.log(`ui-editor.js: ${bytes.length} bytes, ${packages.size} packages, version ${version}`);
