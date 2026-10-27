// The composer as an app has it, for `parity.mjs composer --prepare scripts/flux/composer-grown.mjs`: with the
// runtime's hooks, which fit each box to its text as it arrives (rask-grow.ts). Flux's script fits its boxes on
// load, a wrapped placeholder included — 60px at 390 wide, 32px on a desktop — and a page without the hooks is a
// box of its rows. Nothing is done to either page; both are measured as loaded.

/** Flux's page the composer is measured on. */
export const flux = 'composer';

/** What the Rask page is given of the runtime, as an app has it. */
export const runtime = ['rask-hooks.ts'];

export async function prepare(page) {
  // Fitted on arrival: two frames for the hooks to have read every box.
  await page.evaluate(() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done))));}
