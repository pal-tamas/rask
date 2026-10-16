// Node-driven fixture for the runtime's tablist keyboard (Rask.Core/Resources/rask-tabs.ts `tabAfter`):
// which tab an arrow key lands on, and (`pageSays`) what a tablist does with the page's word on the selected tab. The C# test (TablistKeyboardTests) runs this and asserts the one JSON
// line it prints.
//
// No document is installed, so the module adds no listener on load and only the rule is
// exercised — the focus-and-press half needs a browser and is covered by the site's E2E.

import {pageSays, tabAfter} from "../../../src/Rask.Core/Resources/rask-tabs.js";

const free = [false, false, false];
const middleOff = [false, true, false];
const runOff = [false, false, true, true, false];

// What the reader chose and the page has not answered yet, as the hook keeps it.
function word(before: string | null, after: string | null, told: string[]): {shows: string | null; owed: string} {
    return {shows: pageSays(before, after, told), owed: told.join(",")};
}

console.log(JSON.stringify({
    echo: word("0 profile", "0 account", ["account"]),
    lateEcho: word("0 profile", "0 account", ["account", "billing"]),
    echoPastOne: word("0 profile", "0 billing", ["account", "billing"]),
    pageChose: word("0 profile", "0 billing", []),
    pageChoseOverTheReader: word("0 billing", "1 profile", ["billing"]),
    pageChoseWhatWasOwed: word("0 profile", "1 account", ["billing", "account", "billing"]),
    nothingSaid: word("0 profile", "", ["account"]),
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
