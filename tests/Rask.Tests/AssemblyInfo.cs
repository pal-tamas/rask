// Serialised on purpose.
//
// Every test in this assembly builds a RaskApp, and RaskApp.Build writes PROCESS-WIDE state:
// RaskValidation.AutoValidate is a static property (a Form has no options object to consult and exists
// on both hosts), so an app built with `c.Validation.Off()` turns validation off for every other test
// running at that moment, and the next app built turns it back on underneath the test that asked for it
// off. Run in parallel, that is a flake that reads as "validation is unreliable" rather than as what it
// is. The suite is small and every case here already stands up a real server, so the cost is nil.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
