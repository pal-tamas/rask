using Rask.Core;

// Tell a consuming app's Rask factory generator to emit `global using static Rask.Native.Components.Generated;`
// whenever it references Rask.Native — so native pages call NativeHeaderBar(...) / NativeTabBar(...) with no
// per-file using, exactly like the core element factories. Emission is conditional on this reference being
// present, so non-native apps get no dangling using.
[assembly: RaskFactoryNamespace("Rask.Native.Components")]
