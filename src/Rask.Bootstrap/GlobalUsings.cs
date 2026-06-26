// Framework projects opt out of the generator's global usings (Directory.Build.props sets
// RaskGlobalUsings=false for non-Rask.Example projects), so import the core primitives and the
// generated HTML-tag factories explicitly. This makes Component/Child/Element and Div()/Span()/
// Text/Raw available unqualified throughout Rask.Bootstrap, exactly as a downstream consumer sees
// them — without the per-namespace ambiguity that opting framework projects in would cause.
global using Rask.Core;
global using Rask.Core.Components;
global using static Rask.Core.Components.Generated;
