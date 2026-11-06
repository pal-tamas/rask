// Script fixture for the runtime's command palette (Rask.Core/Resources/rask-command.ts): which commands
// a query leaves (`matches`) and which command an arrow lights (`stepTo`). The C# test
// (CommandPaletteTests) runs this and asserts what it reports.
//
// No document is installed, so the module adds no listener on load and only the rules are exercised — hiding
// the rows and lighting one needs a browser and is UiCommandBrowserTests' in Rask.Server.E2E.Tests.

import {report} from "../../Rask.TestSupport/ScriptFixtures/fixture.js";
import {matches, stepTo} from "../../../src/Rask.Core/Resources/rask-command.js";

const middleOff = [true, false, true, true];

report({
    plain: matches("New invoice", "inv"),
    anyCase: matches("New invoice", "NEW"),
    accentInTheCommand: matches("R\u00e9glages", "reglages"),
    accentInTheQuery: matches("Reglages", "r\u00e9g"),
    nothingTyped: matches("Sign out", ""),
    noMatch: matches("Sign out", "zzz"),
    next: stepTo(middleOff, 0, 1),
    back: stepTo(middleOff, 2, -1),
    stopsForward: stepTo(middleOff, 3, 1),
    stopsBack: stepTo(middleOff, 0, -1),
    first: stepTo(middleOff, -1, 1),
    last: stepTo(middleOff, -1, -1),
    onlyOne: stepTo([false, true, false], 1, 1),
    none: stepTo([false, false], -1, 1),
    empty: stepTo([], -1, 1),
});
