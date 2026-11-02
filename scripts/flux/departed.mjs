// The owner-approved departures from Flux in LOOK (flux-component SKILL.md "Owner-approved departures"), let go by
// every script that compares the kit's computed styles with Flux's (parity.mjs, parity-modal.mjs): each names the
// node by where it sits, and only the named properties are let go there.
// `matches(example, node)` is a predicate on the kit's node and the tree above it; nothing of Flux's is asked.
const holds = tag => (example, node) => example.nodes.some(c => c.parent === node.id && c.tag === tag);
const DEPARTED = [
  // 2026-10-14: Ui.Table's scroll frame — the box holding the <table> itself — is `relative`, so it holds what
  // its cells place absolutely.
  { matches: holds('table'), keys: ['position'] },
  // 2026-11-02, NATIVE FEEL (ui.css): a control selects no text, waits for no second tap and opens no long-press
  // callout. `user-select` inherits in Blink, so what is inside a control computes it too.
  { matches: (example, node) => withAncestors(example, node).some(n => chrome(example, n)), keys: ['userSelect', 'webkitUserSelect', 'touchAction', 'webkitTouchCallout'] },
  // …a picture that is chrome does not drag, and a long press on it opens nothing.
  { matches: (example, node) => node.tag === 'img' && withAncestors(example, node).some(n => marked(n, PICTURED)), keys: ['webkitUserDrag', 'webkitTouchCallout'] },
  { matches: (_, node) => marked(node, ['data-ui-icon']), keys: ['webkitTouchCallout'] },
  // …and an overlay's own scroller keeps its scroll: a modal's dialog, the command palette's list, a menu, a popover.
  { matches: (example, node) => marked(node, SCROLLERS) || (node.tag === 'dialog' && marked(parentOf(example, node) ?? { attrs: {} }, ['data-ui-modal'])), keys: ['overscrollBehaviorY'] },
  // …and the date picker's preset <select>, which is 16px below `sm` as every other field is, where Flux's is 14px —
  // its options with it, whose gap the browser draws in ems.
  { matches: (example, node) => withAncestors(example, node).some(n => n.tag === 'select') && withAncestors(example, node).some(n => marked(n, ['data-ui-date-picker'])), keys: ['fontSize', 'lineHeight', 'gap'] },
];

// The markers ui.css's NATIVE FEEL block names (its first rule): a control's own root.
const CHROME = [
  'data-ui-button', 'data-ui-tab', 'data-ui-navlist-item', 'data-ui-navbar-items', 'data-ui-navmenu-item', 'data-ui-sidebar-item',
  'data-ui-sidebar-toggle', 'data-ui-sidebar-collapse', 'data-ui-sidebar-profile', 'data-ui-sidebar-brand', 'data-ui-breadcrumbs-item',
  'data-ui-accordion-heading', 'data-ui-select-button', 'data-ui-pillbox-trigger', 'data-ui-color-picker-trigger',
  'data-ui-color-picker-button', 'data-ui-date-picker-button', 'data-ui-time-picker-button', 'data-ui-datetime-picker-button',
  'data-ui-checkbox', 'data-ui-radio', 'data-ui-switch', 'data-ui-toggle', 'data-ui-checkbox-buttons', 'data-ui-checkbox-pills',
  'data-ui-radio-buttons', 'data-ui-radio-pills', 'data-ui-radio-segmented', 'data-ui-table-sortable', 'data-ui-profile', 'data-ui-brand',
  'data-ui-avatar',
];
// …and the parts whose BUTTONS (or days) are chrome, not the part itself.
const CHROME_WITHIN = ['data-ui-pagination', 'data-ui-calendar', 'data-rask-calendar', 'data-ui-toast-dialog'];
const PICTURED = ['data-ui-avatar', 'data-ui-brand', 'data-ui-sidebar-brand', 'data-ui-flag'];
const SCROLLERS = ['data-ui-command-items', 'data-ui-menu', 'data-ui-popover'];
const marked = (node, names) => names.some(name => name in node.attrs);
const parentOf = (example, node) => example.nodes.find(n => n.id === node.parent);
function withAncestors(example, node) {
  const chain = [];
  for (let n = node; n; n = parentOf(example, n)) chain.push(n);
  return chain;
}
function chrome(example, node) {
  if (marked(node, CHROME) || node.attrs.role?.startsWith('menuitem')) return true;
  const within = (node.tag === 'button' || node.attrs.role === 'gridcell') && withAncestors(example, node).slice(1).some(n => marked(n, CHROME_WITHIN));
  // A checkbox's, radio's or switch's label beside it in its field.
  const label = marked(node, ['data-ui-label']) && example.nodes.some(c => c.parent === node.parent && marked(c, ['data-ui-checkbox', 'data-ui-radio', 'data-ui-switch']));
  return within || label;
}

/** The style keys an owner-approved departure lets go on the kit's `node` — asked of it and the tree above it in `example`. */
export function departedFrom(example, node) {
  return DEPARTED.filter(d => d.matches(example, node)).flatMap(d => d.keys);
}
