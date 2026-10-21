// Node-driven fixture for what the page is waiting on (rask-owned.ts, `awaiting`): each host takes one per event
// and navigation it sends, and the hooks are told when the last of them is answered.
//
// The C# test (AwaitingTests) runs this in a node subprocess and asserts the JSON line.

import {awaiting, isAwaiting, whenQuiet} from "../../../src/Rask.Core/Resources/rask-owned.js";

let quiet = 0;
whenQuiet(function () { quiet++; });

const idle = isAwaiting();

const first = awaiting();
const second = awaiting();
const whileTwo = isAwaiting();
first();
const afterFirst = {awaiting: isAwaiting(), quiet};
first();
const firstTwice = {awaiting: isAwaiting(), quiet};
second();
const afterBoth = {awaiting: isAwaiting(), quiet};
second();
const secondTwice = quiet;

process.stdout.write(JSON.stringify({idle, whileTwo, afterFirst, firstTwice, afterBoth, secondTwice}));
