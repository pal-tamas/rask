// What the Node followers share: an npm package's latest release, its tarball checked against the integrity
// the registry names, and the files wanted out of it — read in memory, never unpacked to disk.
//
//   const { version, integrity, files } = await latest('cldr-core', path => path === 'package/LICENSE');

import { createHash } from 'node:crypto';
import { gunzipSync } from 'node:zlib';

export async function latest(name, wanted) {
  const release = await json(`https://registry.npmjs.org/${name}/latest`);
  const files = untar(gunzipSync(await download(release.dist.tarball, release.dist.integrity)), wanted);
  return { version: release.version, integrity: release.dist.integrity, files };
}

export async function json(url) {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`${url}: HTTP ${response.status}`);
  return response.json();
}

export async function download(url, integrity) {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`${url}: HTTP ${response.status}`);
  const bytes = Buffer.from(await response.arrayBuffer());
  const digest = `sha512-${createHash('sha512').update(bytes).digest('base64')}`;
  if (digest !== integrity) throw new Error(`${url} is not the tarball the registry names (${digest})`);
  return bytes;
}

// A ustar archive, read in place: 512-byte headers, each followed by its file rounded up to 512. Only the
// files `wanted` names are decoded.
export function untar(archive, wanted) {
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
