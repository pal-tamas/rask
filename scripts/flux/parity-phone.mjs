#!/usr/bin/env node
// Holds Ui.Phone's OPEN country list to Flux UI's live documentation. parity.mjs measures the page as loaded,
// where the list is shut; this opens each example's list — on fluxui.dev and on the page FluxParityPages wrote —
// and compares what is then on screen (open.mjs): the popup subtree with its 244 rows, where it sits against
// the button, a row hovered and pressed.
//
// The rows are not in the page: the phone engine builds them from intl-tel-input's countries when the list
// first opens, as Flux's script does. So the Rask page is SERVED, with the engine and its utils from
// src/Rask.Ui/Resources and the flags from src/Rask.Ui/Flags at the paths an app's build writes them to, and
// it is given the runtime's phone hook, which fetches the engine as it does in an app.
//
// Usage:  dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask page
//         node scripts/flux/parity-phone.mjs                           # every example, light and dark
//         node scripts/flux/parity-phone.mjs --refresh --all           # re-measure Flux, every difference
//
// Exit code 1 on any difference. The measurements land in artifacts/flux-parity/{flux,rask}/phone-open/.

import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, join, normalize } from 'node:path';
import { root } from './lib.mjs';
import { openParity } from './open.mjs';

const kit = join(root, 'src', 'Rask.Ui');
const served = {
  '/js/rask-ui-phone.js': join(kit, 'Resources', 'ui-phone.js'),
  '/js/rask-ui-phone-utils.js': join(kit, 'Resources', 'ui-phone-utils.js'),
};
const types = { '.html': 'text/html', '.js': 'text/javascript', '.svg': 'image/svg+xml', '.css': 'text/css' };

// One origin for the page and what it fetches: a file:// page cannot import a module.
const server = createServer(async (request, response) => {
  const path = decodeURIComponent(new URL(request.url, 'http://x').pathname);
  const file = served[path]
    ?? (path.startsWith('/flags/') ? join(kit, 'Flags', normalize(path.slice('/flags/'.length)).replace(/^(\.\.[/\\])+/, ''))
      : join(root, 'artifacts', 'flux-parity', 'rask', normalize(path).replace(/^(\.\.[/\\])+/, '')));
  try {
    const body = await readFile(file);
    response.writeHead(200, { 'content-type': types[extname(file)] ?? 'application/octet-stream' });
    response.end(body);
  } catch {
    response.writeHead(404);
    response.end();
  }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const port = server.address().port;

await openParity({
  slug: 'phone',
  name: 'phone',
  raskUrl: `http://127.0.0.1:${port}/phone.html`,
  runtime: ['rask-phone.ts'],
  // Every field mounted: the engine marks its picker once it has taken the field over.
  ready: '[data-ui-phone-ready]',
  // A disabled or read-only field opens nothing (measured: Flux's read-only button says aria-readonly and stays shut).
  root: '[data-flux-phone]:has(button[role=combobox]:not([disabled]):not([aria-readonly])), [data-ui-phone]:has(button[role=combobox]:not([disabled]):not([aria-readonly]))',
  trigger: 'button[role=combobox]:not([disabled]):not([aria-readonly])',
  popup: '[data-flux-options], [data-ui-options]',
  rows: '[data-flux-option], [data-ui-option]',
  allRows: '[data-flux-option], [data-ui-option]',
  search: '[data-flux-select-search], [data-ui-select-search]',
  native: {
    'ui-phone': 'div', 'ui-select': 'div', 'ui-selected': 'div', 'ui-options': 'div', 'ui-option': 'div', 'ui-option-empty': 'div',
  },
  says: (control, trigger) => trigger.getAttribute('aria-label'),
  liveRoot: el => el.closest('[data-ui-phone]').parentElement,
  walks: [],
});
