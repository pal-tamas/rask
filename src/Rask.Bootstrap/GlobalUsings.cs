// Framework projects opt out of the generator's global usings (Directory.Build.props sets
// RaskGlobalUsings=false for non-Rask.Example projects), so import the core primitives and the
// generated HTML-tag factories explicitly. This makes Component/Child/Element and Div()/Span()/
// Text/Raw available unqualified throughout Rask.Bootstrap, exactly as a downstream consumer sees
// them — without the per-namespace ambiguity that opting framework projects in would cause.
global using Rask.Core;
global using Rask.Core.Components;
global using static Rask.Core.Components.Generated;

// The Bs* factories the generator emits for this project, so Bs components can compose OTHER Bs
// components (e.g. BsModal/BsAlert reuse BsCloseButton, BsDropdown reuses BsButton) the same way a
// consumer does — no core-class duplication. Bs-prefixed names never clash with the core factories.
global using static Rask.Bootstrap.Generated;
