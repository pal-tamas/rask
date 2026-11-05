// Flux's empty state, for `parity.mjs empty-state --prepare scripts/flux/empty-state.mjs` (#1283). Flux has no empty
// state of its own; its account overview demo draws one when its search matches nothing: a row whose one cell spans
// the table (`colspan="2"`) and holds a status line. No component page shows it, so the demo is brought to it — a
// search for what no transaction is called — and its table is set in an example frame of the docs page's size
// (FluxParityPages' reset), out of the card, where the kit's `EmptyStateParity` stands. The card it leaves is the
// demo's, not the table's: the bleed falls back to 1.5rem on both pages.

/** Flux's page the state is measured on: a demo, not a component page. */
export const flux = 'demo/account-overview';

/** The empty state needs no runtime. */
export const runtime = [];

export async function prepare(page, scheme, side) {
  if (side !== 'flux') return;

  await page.fill('input[placeholder="Search transactions"]', 'no transaction is called this');
  await page.waitForSelector('[data-flux-table] [role=status]', { timeout: 30000 });
  await page.evaluate(() => {
    const box = document.querySelector('[data-flux-table]').closest('[data-flux-table-bleed]');
    const wrapper = document.createElement('div');
    wrapper.setAttribute('data-preview-wrapper', '');
    wrapper.dataset.section = 'empty';
    // FluxParityPages' frame: 654px wide with 24px a side on a desktop, the screen less 25px a side on a phone.
    const phone = innerWidth < 640;
    Object.assign(wrapper.style, { padding: '64px 24px', overflowX: 'auto', boxSizing: 'border-box' },
      phone ? { width: 'auto', marginInline: '25px' } : { width: '654px', marginInline: 'auto' });
    wrapper.append(box.cloneNode(true));
    // The demo's layout goes (its body is the sidebar's grid); its ink and ground stay, as a docs page's would.
    document.body.replaceChildren(wrapper);
    document.body.style.display = 'block';
    scrollTo(0, 0);
  });
  await page.evaluate(() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done))));
}
