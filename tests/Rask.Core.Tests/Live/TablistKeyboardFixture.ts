// Node-driven fixture for the runtime's tablist keyboard (Rask.Core/Resources/rask-tabs.ts `tabAfter`):
// which tab an arrow key lands on. The C# test (TablistKeyboardTests) runs this and asserts the one JSON
// line it prints.
//
// No document is installed, so the module adds no listener on load and only the rule is
// exercised — the focus-and-press half needs a browser and is covered by the site's E2E.

import {tabAfter} from "../../../src/Rask.Core/Resources/rask-tabs.js";

const free = [false, false, false];
const middleOff = [false, true, false];
const runOff = [false, false, true, true, false];

console.log(JSON.stringify({
    next: tabAfter(free, 0, 1),
    previous: tabAfter(free, 2, -1),
    wrapsForward: tabAfter(free, 2, 1),
    wrapsBack: tabAfter(free, 0, -1),
    skipsForward: tabAfter(middleOff, 0, 1),
    skipsBack: tabAfter(middleOff, 2, -1),
    skipsARun: tabAfter(runOff, 1, 1),
    skipsARunBack: tabAfter(runOff, 4, -1),
    skipsAtTheEnd: tabAfter([false, false, true], 1, 1),
    fromNowhereForward: tabAfter(free, -1, 1),
    fromNowhereBack: tabAfter(free, -1, -1),
    onlyOneLeft: tabAfter([true, false, true], 1, 1),
    noneLeft: tabAfter([true, true], 0, 1),
    empty: tabAfter([], -1, 1),
}));
