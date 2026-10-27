// The command palette once it is used, for `parity.mjs command-open --prepare scripts/flux/command-open.mjs`
// (parity-command.mjs runs it). A palette as loaded shows no lit command, no filter, no clear button and no
// open modal, so both pages are brought to those states — the same way, with the same keys and presses — before
// they are measured. Flux's page holds ONE inline palette, so its first example is copied twice (its <ui-select>
// is a custom element, and a copy is one in its own right); CommandOpenParity writes the Rask page with three.
//
//   typed       "ch" typed, the box left: one command shown, the clear button there, nothing lit
//   empty       "zz" typed: no command shown, "No results found"
//   as-a-modal  the modal opened from its trigger, the box pressed, ArrowDown: the second command lit, the close
//               button in the box's corner

/** Flux's page the states are measured on. */
export const flux = 'command';

/**
 * A browser that SHOWS its scrollbars, as a reader's does (parity-modal.mjs): headless hides them, and there Flux's
 * page lock reserves a gutter nothing was using, so its modal sits 7.5px off centre and the kit's does not.
 */
export const launch = { ignoreDefaultArgs: ['--hide-scrollbars'] };

/** What the Rask page is given of the runtime, as an app has it: the hooks, which the palette and the modal ask for. */
export const runtime = ['rask-hooks.ts'];

const SECTIONS = ['typed', 'empty', 'as-a-modal'];
const BOX = '[data-flux-command] input, [data-ui-command] input';
const TRIGGER = '[data-flux-modal-trigger] button, [data-ui-modal-trigger] button';

// Settled: two frames, and what they started given the time to land.
const settle = page => page.evaluate(() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(() => setTimeout(done, 250)))));

export async function prepare(page, scheme, side) {
  // On a phone (parity.mjs --view 390x844) a press is a tap.
  const phone = page.viewportSize().width < 640;
  const press = async el => (phone ? el.tap() : el.click());
  if (side === 'flux') {
    await page.evaluate(sections => {
      const [inline, modal] = document.querySelectorAll('[data-preview-wrapper]');
      for (const section of sections.slice(0, 2)) {
        const copy = inline.cloneNode(true);
        copy.dataset.section = section;
        modal.before(copy);
      }
      inline.removeAttribute('data-preview-wrapper');
      modal.dataset.section = sections[2];
    }, SECTIONS);
    await settle(page);
  }

  // More page than the viewport, as Flux's has: it scrolls, and an open modal is centred beside the scrollbar's gutter.
  if (side === 'rask') await page.evaluate(() => { document.body.style.paddingBottom = '100vh'; });
  const wrappers = await page.$$('[data-preview-wrapper]');
  for (const [nth, text] of [[0, 'ch'], [1, 'zz']]) {
    await press(await wrappers[nth].$(BOX));
    await page.keyboard.type(text, { delay: 40 });
    await page.evaluate(() => document.activeElement?.blur());
    await settle(page);
  }

  await press(await wrappers[2].$(TRIGGER));
  await settle(page);
  await press(await page.$(`dialog[open] ${BOX.split(', ').join(', dialog[open] ')}`));
  await page.keyboard.press('ArrowDown');
  if (!phone) await page.mouse.move(1, 1);
  await settle(page);
}
