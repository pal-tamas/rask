// RaskValidation.AutoValidate is a process-wide switch, and the tests that prove the global opt-out
// works have to flip it. xUnit runs test CLASSES in parallel by default, so leaving that on would let
// one class turn validation off underneath every other class in this assembly — a flake that shows up
// as an unrelated test finding no validation messages, which reads as a broken validator rather than a
// broken test. The suite is small; serialising it costs nothing worth measuring.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
