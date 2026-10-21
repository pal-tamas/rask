// Ui.Filters' list laid out as Flux's full-bleed table (fluxui.dev/components/table#full-bleed), measured side by side
// at 1920 and 390: the card, its header row, the button on the right, the gap to the table, and the table bleeding
// through the card's padding. What sits between the header and the table (the filter badges) is Rask's own and is
// not compared; neither is the button's label.
//
//   node scripts/flux/parity-filters-bleed.mjs http://127.0.0.1:PORT
import { chromium } from './lib.mjs';

const ours = process.argv[2] ?? 'http://127.0.0.1:5050';

// Every length relative to the card's own box, so the two pages' different surroundings do not count.
const MEASURE = `(card) => {
  const r = el => el.getBoundingClientRect();
  const c = r(card);
  const row = card.querySelector(':scope > div');
  const button = row.querySelector('button');
  const table = card.querySelector('table');
  const th = table.querySelector('th');
  const words = document.createRange();
  const walker = document.createTreeWalker(th, NodeFilter.SHOW_TEXT, { acceptNode: n => n.textContent.trim() ? 1 : 3 });
  words.selectNodeContents(walker.nextNode());
  const cs = getComputedStyle(card);
  return {
    cardPadding: [cs.paddingTop, cs.paddingRight, cs.paddingBottom, cs.paddingLeft].join(' '),
    cardRadius: cs.borderRadius,
    rowHeight: Math.round(r(row).height),
    rowAlign: getComputedStyle(row).alignItems + ' ' + getComputedStyle(row).justifyContent,
    buttonHeight: Math.round(r(button).height),
    buttonRightInset: Math.round(c.right - r(button).right),
    buttonTop: Math.round(r(button).top - c.top),
    tableLeft: Math.round(r(table).left - c.left),
    tableRightInset: Math.round(c.right - r(table).right),
    firstHeadingTextLeft: Math.round(words.getBoundingClientRect().left - c.left),
    headingToTableGap: Math.round(r(table).top - r(row).bottom),
  };
}`;

// At a phone's width these follow the words, not the layout: Flux's subtitle wraps to three lines (a taller row, the
// button centred lower) and its four wider columns scroll inside the card. They are printed, not judged.
const WORDS = { 390: ['rowHeight', 'buttonTop', 'tableRightInset'] };

const browser = await chromium().launch();
let failed = 0;
for (const width of [1920, 390]) {
  const page = await browser.newPage({ viewport: { width, height: 1000 } });

  await page.goto('https://fluxui.dev/components/table', { waitUntil: 'networkidle' });
  const flux = await page.locator('[data-flux-card]', { hasText: 'Recent customers' }).first().evaluate(new Function(`return ${MEASURE}`)());

  // Our list with no filter in force, so nothing stands between the header and the table, as in Flux's.
  await page.goto(ours + '/ui/filters/', { waitUntil: 'networkidle' });
  const card = page.locator('[data-testid="ui-filters"] [data-ui-card]').first();
  await card.locator('tbody tr').first().waitFor();
  const rask = await card.evaluate(new Function(`return ${MEASURE}`)());

  for (const key of Object.keys(flux)) {
    const same = String(flux[key]) === String(rask[key]);
    const words = (WORDS[width] ?? []).includes(key);
    failed += same || words ? 0 : 1;
    console.log(`${width} ${same ? 'ok  ' : words ? 'text' : 'DIFF'} ${key.padEnd(22)} flux ${flux[key]}  rask ${rask[key]}`);
  }

  await page.close();
}

await browser.close();
process.exit(failed ? 1 : 0);
