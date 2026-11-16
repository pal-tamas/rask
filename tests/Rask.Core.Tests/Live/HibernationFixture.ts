// Drives rask-hibernate.ts — what a hidden tab answers when the server offers to let its session go — and prints
// what it said as JSON for HibernationFixtureTests.

import {report} from "../../Rask.TestSupport/ScriptFixtures/fixture.js";
import {hibernationRefusal, type HibernationFacts} from "../../../src/Rask.Server/Resources/rask-hibernate.js";

const quiet: HibernationFacts = {hidden: true, unsaved: false, media: false, calls: 0, busy: false, waiting: 0};

report({
    quiet: hibernationRefusal(quiet),
    shown: hibernationRefusal({...quiet, hidden: false}),
    unsaved: hibernationRefusal({...quiet, unsaved: true}),
    media: hibernationRefusal({...quiet, media: true}),
    interop: hibernationRefusal({...quiet, calls: 1}),
    busy: hibernationRefusal({...quiet, busy: true}),
    waiting: hibernationRefusal({...quiet, waiting: 2}),
    // Shown wins over everything: the reader is back, and nothing else is worth naming.
    shownAndUnsaved: hibernationRefusal({...quiet, hidden: false, unsaved: true, media: true}),
    // An edit in hand is named before what it is waiting on.
    unsavedAndBusy: hibernationRefusal({...quiet, unsaved: true, busy: true, calls: 3}),
});
