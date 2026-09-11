// This test assembly stands in for an app built with RaskDevSession=true: EditorDevSession reads exactly this
// metadata, and there is no other way to hand it an assembly that carries it. Tests that need an ordinary
// build pass typeof(object).Assembly, which carries none.
[assembly: System.Reflection.AssemblyMetadata("Rask.DevSession", "true")]
