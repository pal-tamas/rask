#!/usr/bin/env node
// Holds Ui.Pillbox's OPEN list to Flux UI's live documentation: parity.mjs measures the page as loaded, where
// the list is not displayed. The measuring, the comparison and the walk are open.mjs's; what is the
// pillbox's own — its selectors and its walks — is here.
//
// Usage:  dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask page
//         node scripts/flux/parity-pillbox.mjs [--refresh] [--all]
//         node scripts/flux/parity-pillbox.mjs --record                # Flux's walks alone, every step
//         node scripts/flux/parity-pillbox.mjs --live URL-OF-THE-DEMO  # the walks, against a running site
//
// Exit code 1 on any difference. The measurements land in artifacts/flux-parity/{flux,rask}/pillbox-open/.

import { openParity } from './open.mjs';

await openParity({
  slug: 'pillbox',
  raskPage: 'pillbox-picked',
  pick: [1, 2],
  // A click on a row takes focus to the list; the input among the pills keeps it.
  focused: ['[data-ui-pillbox-trigger] [data-ui-pillbox-input]', '[role=listbox]'],
  name: 'pillbox',
  root: '[data-flux-pillbox], [data-ui-pillbox]',
  trigger: '[data-flux-pillbox-trigger], [data-ui-pillbox-trigger]',
  popup: '[popover]',
  rows: '[data-flux-listbox-option], [data-ui-listbox-option]',
  allRows: '[data-flux-listbox-option], [data-ui-listbox-option], [data-flux-option-create], [data-ui-option-create]',
  search: '[data-flux-pillbox-search], [data-ui-pillbox-search]',
  remove: 'ui-selected-remove, [data-ui-pillbox-trigger] [data-value] > :last-child',
  // Flux's custom elements and the native element Rask.Ui writes in their place, with the same role.
  native: {
    'ui-pillbox': 'div', 'ui-pillbox-trigger': 'div', 'ui-selected': 'div', 'ui-options': 'div', 'ui-option': 'div',
    'ui-option-empty': 'div', 'ui-option-create': 'div', 'ui-selected-remove': 'div',
  },
  // The pills, then what the inline input holds.
  says: (control, trigger, words) => words(trigger) + (trigger.querySelector('input') ? ` [${trigger.querySelector('input').value}]` : ''),
  liveRoot: el => el.closest('[data-ui-pillbox]').parentElement,
  walks: [
    {
      name: 'default', flux: 0, rask: '#ui-pillbox-tags',
      keys: ['focus', 'ArrowDown', 'ArrowDown', 'ArrowDown', 'ArrowUp', 'End', 'Home', 'Enter', 'ArrowDown', 'Enter', 'Enter', 'Escape',
        'Backspace', 'type:s', 'ArrowDown', 'ArrowDown', 'hover:1', 'ArrowDown', 'click:4', 'Escape', 'Enter', 'Tab'],
    },
    {
      // Space opens the closed list on both. On Rask the page behind it scrolls as well: the trigger is no
      // button, and nothing can prevent a key's default from C#. See FluxConformanceTests.NotTranslated.
      name: 'Space on the closed trigger', flux: 0, rask: '#ui-pillbox-tags',
      keys: ['focus', 'Space', 'Escape'],
    },
    {
      name: 'pointer', flux: 0, rask: '#ui-pillbox-tags',
      keys: ['click', 'click:1', 'leave', 'ArrowDown', 'hover:3', 'leave', 'ArrowDown', 'Escape', 'remove:0', 'click', 'click:2', 'click:3', 'Escape', 'remove:0'],
    },
    {
      name: 'searchable', flux: 2, rask: '#ui-pillbox-searchable',
      keys: ['click', 'type:p', 'ArrowDown', 'Enter', 'type:zz', 'Backspace', 'Backspace', 'ArrowDown', 'Enter', 'Escape', 'ArrowDown', 'Tab'],
    },
    {
      name: 'combobox', flux: 4, rask: '#ui-pillbox-combobox',
      keys: ['click', 'ArrowDown', 'Enter', 'type:ru', 'Enter', 'type:zz', 'Backspace', 'Backspace', 'Backspace', 'Backspace', 'Escape', 'Escape',
        'ArrowDown', 'Tab'],
    },
  ],
});
