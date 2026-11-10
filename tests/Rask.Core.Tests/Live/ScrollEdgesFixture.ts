// Script fixture for the runtime's scroll edges (Rask.Core/Resources/rask-scroll-edges.ts `edges`): which sides of a
// table's scroll area hide something, the marks a sticky column's shadow is drawn on. The C# test (ScrollEdgesTests)
// runs this and asserts what it reports.
//
// No document is installed, so the module adds no listener on load and only the rule is exercised — that the marks
// land on a scrolled table and draw its shadow is the site's E2E (UiKitDataDisplayTests).

import {report} from "../../Rask.TestSupport/ScriptFixtures/fixture.js";
import {edges} from "../../../src/Rask.Core/Resources/rask-scroll-edges.js";

// A table 1000px wide in a box 400px wide: 600px to scroll.
report({
    atStart: edges(0, 1000, 400, false),
    inTheMiddle: edges(300, 1000, 400, false),
    atTheEnd: edges(600, 1000, 400, false),
    aPixelIn: edges(2, 1000, 400, false),
    aPixelShortOfTheEnd: edges(598, 1000, 400, false),
    withinSlackOfTheStart: edges(0.5, 1000, 400, false),
    withinSlackOfTheEnd: edges(599.5, 1000, 400, false),
    fits: edges(0, 400, 400, false),
    fitsButForAFraction: edges(0, 400.75, 400, false),
    widerThanItsTable: edges(0, 300, 400, false),
    grownWider: edges(0, 1200, 400, false),
    shrunkToFit: edges(300, 400, 400, false),
    rtlAtStart: edges(0, 1000, 400, true),
    rtlInTheMiddle: edges(-300, 1000, 400, true),
    rtlAtTheEnd: edges(-600, 1000, 400, true),
    rtlFits: edges(0, 400, 400, true),
});
