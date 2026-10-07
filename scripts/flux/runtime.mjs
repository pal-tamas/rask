// Rask's runtime behaviour, for a parity page.
//
// A parity page is static markup: the kit's sheet and nothing else. What Flux does in script the kit asks the
// RUNTIME for, by attribute (data-rask-tooltip, data-rask-dismiss-after, data-rask-dismiss-scope —
// docs/js-interop-runtime.md), so a page that is walked with pointer and keyboard needs those modules, exactly
// as an app has them. They are bundled here from their sources with the esbuild the build already caches,
// and added to the page as one script: no transport, no session — only the delegated listeners.

import { spawnSync } from 'node:child_process';
import { existsSync, readdirSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';
import { root } from './lib.mjs';

const resources = join(root, 'src', 'Rask.Core', 'Resources');

// RASK_FLUX_ESBUILD names the binary; otherwise the newest one the build has cached (Rask.Core.targets).
function esbuild() {
  if (process.env.RASK_FLUX_ESBUILD) return process.env.RASK_FLUX_ESBUILD;
  const cache = join(process.env.RaskTypeScriptCacheRoot ?? join(homedir(), '.rask', 'typescript'), 'esbuild');
  const found = existsSync(cache)
    ? readdirSync(cache).sort().reverse()
      .flatMap(version => readdirSync(join(cache, version)).map(platform => join(cache, version, platform, 'bin', 'esbuild')))
      .find(existsSync)
    : null;
  if (!found) {
    console.error('flux: no esbuild — build src/Rask.Server once (it caches one), or set RASK_FLUX_ESBUILD.');
    process.exit(1);
  }

  return found;
}

// `modules` are file names under src/Rask.Core/Resources, imported for their side effects.
export function runtime(...modules) {
  const entry = modules.map(name => `import ${JSON.stringify('./' + name)};`).join('\n');
  const built = spawnSync(esbuild(), ['--bundle', '--format=iife', '--target=es2019', '--log-level=warning', '--loader=ts'],
    { input: entry, cwd: resources, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  if (built.status !== 0) {
    console.error(`flux: bundling ${modules.join(', ')} failed\n${built.stderr}`);
    process.exit(1);
  }

  return built.stdout;
}

// Adds them to a loaded page.
export async function withRuntime(page, ...modules) {
  await page.addScriptTag({ content: runtime(...modules) });
}
