#!/usr/bin/env node
// Holds Ui.Autocomplete's OPEN list to Flux UI's live documentation: parity.mjs measures the page as loaded,
// where the list is not displayed. The measuring, the comparison and the walk are open.mjs's; what is the
// autocomplete's own — its selectors and its walks — is here.
//
// Usage:  dotnet test tests/Rask.Ui.Tests --filter FluxParityPages          # writes the Rask page
//         node scripts/flux/parity-autocomplete.mjs [--refresh] [--all]
//         node scripts/flux/parity-autocomplete.mjs --record                # Flux's walks alone, every step
//         node scripts/flux/parity-autocomplete.mjs --live URL-OF-THE-DEMO  # the walks, against a running site
//
// Exit code 1 on any difference. The measurements land in artifacts/flux-parity/{flux,rask}/autocomplete-open/.

import { openParity } from './open.mjs';

await openParity({
  slug: 'autocomplete',
  name: 'autocomplete',
  root: '[data-flux-autocomplete], [data-ui-autocomplete]',
  trigger: 'input[role=combobox]',
  popup: '[data-flux-autocomplete-items], [data-ui-autocomplete-items]',
  rows: '[data-flux-autocomplete-item], [data-ui-autocomplete-item]',
  // Flux's custom elements and the native element Rask.Ui writes in their place, with the same role.
  native: { 'ui-select': 'div', 'ui-field': 'div', 'ui-label': 'label', 'ui-options': 'div', 'ui-option': 'div', 'ui-empty': 'div' },
  says: (control, trigger) => trigger.value,
  liveRoot: el => el.closest('[data-ui-autocomplete]').parentElement,
  walks: [
    {
      name: 'keys', flux: 0, rask: '#ui-autocomplete-state',
      keys: ['focus', 'ArrowDown', 'ArrowDown', 'ArrowUp', 'ArrowUp', 'End', 'Home', 'Enter', 'ArrowDown', 'Escape', 'Escape',
        'type:x', 'Backspace', 'Backspace', 'Backspace', 'Backspace', 'Backspace', 'Backspace', 'Backspace', 'Backspace',
        'type:ne', 'ArrowDown', 'ArrowDown', 'Enter', 'type:zz', 'Enter', 'Tab'],
    },
    {
      name: 'pointer', flux: 0, rask: '#ui-autocomplete-state',
      keys: ['click', 'hover:2', 'ArrowDown', 'click:4', 'click', 'type:x', 'Escape', 'click', 'Tab'],
    },
  ],
});
