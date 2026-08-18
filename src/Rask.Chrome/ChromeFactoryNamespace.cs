using Rask.Core;

// Tell a consuming app's Rask factory generator to emit
// `global using static Rask.Chrome.Components.Generated;` whenever it references Rask.Chrome — so a screen
// writes AppBar.Title(...) / TabStrip.Tabs([...]) with no per-file using, exactly like the core element
// entries. Emission is conditional on the reference being present, so an app that never names a bar gets no
// dangling using. (The `Screen` base class itself is an ordinary type, so a screen file still says
// `using Rask.Chrome;` — a global using static covers the chain entries, not type names.)
[assembly: RaskFactoryNamespace("Rask.Chrome.Components")]
