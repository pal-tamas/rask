#!/usr/bin/env node
// Holds Ui.Select's OPEN listbox to Flux UI's live documentation. parity.mjs measures every example as
// loaded, where a custom select's options are not displayed; this opens each one — on fluxui.dev and on the
// page FluxParityPages wrote — and compares what is then on screen:
//   - the trigger and the whole popup subtree: tag, box, computed styles, pseudo-elements;
//   - where the popup sits against its trigger (every box is measured from the trigger's corner);
//   - a row hovered, and a row pressed.
//
// The measuring, the comparison and the walk are open.mjs's, shared with every control that opens a list;
// what is the select's own — its selectors and its walks — is here.
//
// Usage:  dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask page
//         node scripts/flux/parity-select.mjs                          # every custom example, light and dark
//         node scripts/flux/parity-select.mjs --refresh                # re-measure Flux
//         node scripts/flux/parity-select.mjs --section combobox --all # one section, every difference
//
// Exit code 1 on any difference. The measurements land in artifacts/flux-parity/{flux,rask}/select-open/.

import { openParity } from './open.mjs';

await openParity({
  slug: 'select',
  name: 'select',
  root: '[data-flux-select], [data-ui-select]',
  trigger: 'button[role=combobox], input[role=combobox]',
  popup: '[data-flux-options], [data-ui-options]',
  rows: '[data-flux-option], [data-ui-option]',
  allRows: '[data-flux-option], [data-ui-option], [data-flux-option-create], [data-ui-option-create]',
  search: '[data-flux-select-search], [data-ui-select-search]',
  // Flux's custom elements and the native element Rask.Ui writes in their place, with the same role.
  native: {
    'ui-select': 'div', 'ui-selected': 'div', 'ui-options': 'div', 'ui-option': 'div', 'ui-option-empty': 'div',
    'ui-option-create': 'div',
  },
  says: (control, trigger, words) => (trigger.localName === 'input' ? trigger.value : words(trigger)),
  liveRoot: el => el.closest('[data-ui-select]').parentElement,
  // Each walk: Flux's example by index, the showcase's control by selector, and the steps.
  walks: [
    {
      name: 'listbox', flux: 3, rask: '#ui-select-listbox',
      keys: ['focus', 'ArrowDown', 'ArrowDown', 'ArrowDown', 'ArrowUp', 'End', 'Home', 'PageDown', 'PageUp', 'Enter',
        'ArrowUp', 'ArrowDown', 'ArrowDown', 'ArrowDown', 'ArrowDown', 'ArrowDown', 'ArrowDown', 'Escape', 'type:le', 'Space', 'type:acc', 'Space',
        'ArrowDown', 'hover:1', 'ArrowDown', 'click:4', 'ArrowDown', 'Escape', 'ArrowDown', 'Tab'],
    },
    {
      // Enter on the CLOSED button. Flux leaves the list shut; a native <button popovertarget> is pressed by
      // Enter, and C# cannot prevent a key's default. The runtime hook that would (contain Enter on a closed
      // listbox button) does not exist — see FluxConformanceTests.NotTranslated. Printed, and not counted.
      name: 'listbox, Enter on the closed button', flux: 3, rask: '#ui-select-listbox',
      keys: ['focus', 'Enter'],
      accepted: 'the browser presses a button on Enter; Flux\'s script does not open on it',
    },
    {
      name: 'searchable', flux: 10, rask: '#ui-select-searchable',
      keys: ['focus', 'ArrowDown', 'ArrowDown', 'type:co', 'ArrowDown', 'ArrowUp', 'ArrowUp', 'type:zz', 'Backspace', 'Backspace', 'Backspace',
        'Enter', 'ArrowDown', 'type:leg', 'Escape', 'ArrowDown', 'type:á', 'Tab'],
    },
    {
      name: 'multiple', flux: 12, rask: '#ui-select-multiple',
      keys: ['focus', 'ArrowDown', 'ArrowDown', 'Enter', 'ArrowDown', 'Enter', 'Enter', 'Escape', 'type:o', 'ArrowDown', 'Tab'],
    },
    {
      name: 'combobox', flux: 13, rask: '#ui-select-combobox',
      keys: ['click', 'Escape', 'type:de', 'ArrowDown', 'Enter', 'ArrowDown', 'ArrowDown', 'Escape', 'Escape', 'type:xyz', 'Tab'],
    },
  ],
});
