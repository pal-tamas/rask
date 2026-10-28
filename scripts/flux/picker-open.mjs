// A picker measured open, for `parity.mjs <slug>-open --prepare scripts/flux/picker-open.mjs` (parity-date.mjs
// runs it for the date, time and datetime pickers). Flux's popup is placed by its script: turned over a trigger
// with no room under it, held to the height the viewport leaves it. The kit's is placed by the runtime's hooks
// (rask-place.ts), so the Rask page is given them, as an app has them — without, its popup hangs where the
// anchor rules alone put it, as tall as its content, and is compared with a panel no app would show.

/** Flux's page: the one parity.mjs was asked for, less `-open`. */
export const flux = process.argv[2].replace(/-open$/, '');

/**
 * A browser that SHOWS its scrollbars, as a reader's does (parity-date.mjs says why): headless hides them, Flux's
 * page lock then reserves a gutter no scrollbar takes, and the page under its open dialog reflows by 15px.
 */
export const launch = { ignoreDefaultArgs: ['--hide-scrollbars'] };

/** What the Rask page is given of the runtime, as an app has it. */
export const runtime = ['rask-hooks.ts'];

/** Flux's page has its pickers marked as the Rask page writes them (data-parity-open). */
export async function prepare(page, scheme, side) {
  if (side === 'flux') await page.evaluate(mark, flux);
}

/**
 * Runs in the page. Marks what a reader presses to open each picker: the picker itself where its trigger fills it
 * (a press at the centre lands on the button), or the button beside the typed fields where the trigger is an
 * input — a press on a field only puts the caret in it.
 */
export function mark(slug) {
  for (const picker of document.querySelectorAll(`[data-preview-wrapper] [data-flux-${slug}]`)) {
    // …a button where the trigger has one, else the chevron at the end of the typed field.
    const beside = picker.querySelector(`[data-flux-${slug}-trigger] button, ui-${slug}-trigger button`)
      ?? picker.querySelector(`ui-${slug}-trigger > svg:last-child`);
    (beside ?? picker).setAttribute('data-parity-open', '');
  }
}
