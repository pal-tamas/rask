// The value-object bases this package maps to columns live in the `Rask` package (Rask.Values), so a browser app
// declares its values without EF Core. `Range` is Rask's base here, as it is in an app (the package's props alias it).
global using Rask.Values;
global using Range = Rask.Values.Range;
