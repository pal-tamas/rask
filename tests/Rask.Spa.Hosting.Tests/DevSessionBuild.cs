// This test assembly stands in for an app built with RaskDevSession=true: EditorDevSession reads exactly this
// metadata, and there is no other way to hand it an assembly that carries it. The entry assembly under the test
// runner is the runner's own, so no host these tests start is affected; tests that need an ordinary build pass
// typeof(object).Assembly.
[assembly: System.Reflection.AssemblyMetadata("Rask.DevSession", "true")]
