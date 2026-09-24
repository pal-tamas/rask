// Namespaces every file in this app sees without a `using` of its own. `Rask` is the framework's front door:
// the UI kit (`Ui.Button`, `Ui.Tone`) and everything else a page reaches for by name. `Rask.Markup` is
// every element and markup primitive, so `Div[…]` reads the same in a helper class as in a component.
global using Rask;
global using static Rask.Markup;
