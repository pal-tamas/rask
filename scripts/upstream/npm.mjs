// What the node followers share: npm's latest release of a package, its tarball checked against the registry's
// integrity and read in memory, and what to do when the registry does not answer (leave every file as it is).
//
// RASK_UPSTREAM_FIXTURES names a directory of saved answers, one file per URL (fixtureName, the same name
// scripts/upstream/upstream.py gives one), which is how scripts/tests/upstream-node-followers.test.sh runs a
// follower with no network. A URL with no saved answer is a registry that did not answer.

import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { join } from 'node:path';

const saved = process.env.RASK_UPSTREAM_FIXTURES;

/** The file a saved answer for `url` is kept in. */
export function fixtureName(url) {
  return url.replace(/^[^:]*:\/\//, '').replace(/[^A-Za-z0-9.-]+/g, '_');
}

/** npm's latest stable release of `name`: its manifest, with `version` and `dist`. */
export function latest(name, label) {
  return release(name, 'latest', label);
}

/** One release of `name` (a version, or a dist-tag), as the registry describes it. */
export async function release(name, version, label) {
  const manifest = JSON.parse((await get(`https://registry.npmjs.org/${name.replace('/', '%2f')}/${version}`, label)).toString('utf8'));
  if (!/^\d+\.\d+\.\d+$/.test(manifest.version ?? '')) fail(label, `npm's ${version} ${name} is ${manifest.version}, not a stable release`);
  if (!manifest.dist?.tarball || !manifest.dist?.integrity?.startsWith('sha512-')) fail(label, `${name} ${manifest.version} names no sha512 tarball`);
  return manifest;
}

/** The release's tarball, gunzipped later by the caller; refused unless it hashes to the registry's integrity. */
export async function tarball(release, label) {
  const bytes = await get(release.dist.tarball, label);
  const digest = `sha512-${createHash('sha512').update(bytes).digest('base64')}`;
  if (digest !== release.dist.integrity) fail(label, `${release.dist.tarball} is not the tarball the registry names (${digest})`);
  return bytes;
}

/**
 * A ustar archive, read in place: 512-byte headers, each followed by its file rounded up to 512. Only the
 * regular files `wanted` names are decoded, as UTF-8.
 */
export function untar(archive, wanted = () => true) {
  const entries = new Map();
  const field = (at, start, length) => archive.subarray(at + start, at + start + length).toString('utf8').replace(/\0.*$/s, '');
  for (let at = 0; at + 512 <= archive.length;) {
    const name = field(at, 0, 100);
    if (!name) break;
    const size = parseInt(field(at, 124, 12).trim() || '0', 8);
    const prefix = field(at, 345, 155);
    const path = prefix ? `${prefix}/${name}` : name;
    if ((field(at, 156, 1) === '0' || field(at, 156, 1) === '') && wanted(path)) {
      entries.set(path, archive.subarray(at + 512, at + 512 + size).toString('utf8'));
    }

    at += 512 + Math.ceil(size / 512) * 512;
  }

  return entries;
}

export function fail(label, message) {
  console.error(`${label}: ${message}`);
  process.exit(1);
}

// A registry that is down, slow or limiting is tomorrow's run: exit 0, nothing written.
function unanswered(label, url, why) {
  console.error(`${label}: ${url} did not answer (${why}); leaving the files as they are.`);
  process.exit(0);
}

/** What `url` answers, as bytes; a saved answer under RASK_UPSTREAM_FIXTURES. No answer exits 0. */
export async function get(url, label) {
  if (saved) {
    return readFile(join(saved, fixtureName(url))).catch(() => unanswered(label, url, 'no saved answer'));
  }

  let response;
  try {
    response = await fetch(url, { headers: { 'User-Agent': 'rask-upstream' }, signal: AbortSignal.timeout(300_000) });
  } catch (error) {
    unanswered(label, url, error.cause?.code ?? error.message);
  }

  if (response.status === 429 || response.status >= 500) unanswered(label, url, `HTTP ${response.status}`);
  if (!response.ok) fail(label, `${url}: HTTP ${response.status}`);
  return Buffer.from(await response.arrayBuffer());
}
