// RaskValidators is a process-wide registry, and this suite writes to it: each test points a model
// type at its own rules. xUnit runs test CLASSES in parallel by default, so leaving that on would let
// two classes registering different validators for the same model race — a flake that surfaces as the
// wrong error message rather than as a registration problem.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
